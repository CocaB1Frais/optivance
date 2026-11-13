using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Optivance.Core;

namespace Optivance.Infrastructure;

/// <summary>
/// Analyse MFT d'un volume NTFS (D-06) : le helper est lancé avec une demande UAC à chaque
/// analyse, jamais mémorisée ; l'interface reste non élevée. Le helper n'est accepté sur le
/// canal que si son PID et son chemin d'image correspondent au processus lancé.
/// </summary>
public sealed class MftScanClient
{
    public const string HelperFileName = "optivance-mft-helper.exe";
    private const int ErrorCancelled = 1223;

    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, HelperFileName);

    public static bool IsHelperPresent => File.Exists(HelperPath);

    /// <summary>Vrai si <paramref name="target"/> est la racine d'un volume local NTFS prêt.</summary>
    public static bool CanScan(string target, out char letter)
    {
        letter = '\0';
        try
        {
            var full = Path.GetFullPath(target);
            if (!string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase) || full.Length != 3 || !char.IsAsciiLetter(full[0]))
            {
                return false;
            }
            var drive = new DriveInfo(full);
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || drive.DriveFormat != "NTFS")
            {
                return false;
            }
            letter = char.ToUpperInvariant(full[0]);
            return IsHelperPresent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public async Task<ScanResult> ScanVolumeAsync(char letter, IProgress<ScanUpdate>? progress, CancellationToken cancellationToken)
    {
        if (!char.IsAsciiLetter(letter))
        {
            throw new ArgumentException("Lettre de volume invalide.", nameof(letter));
        }
        var helperPath = HelperPath;
        if (!File.Exists(helperPath))
        {
            throw new ScanEngineException("helper-missing", "Le composant d’analyse MFT est absent du dossier de l’application.");
        }

        var total = Stopwatch.StartNew();
        var pipeName = $"optivance-mft-{Guid.NewGuid():N}";
        await using var pipe = SecurePipeServer.CreateCurrentUserOnly(pipeName);
        cancellationToken.ThrowIfCancellationRequested();

        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = $"--pipe-name {pipeName} --volume {char.ToUpperInvariant(letter)}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new ScanEngineException("uac-declined", "Élévation refusée : l’analyse rapide MFT n’a pas été autorisée.");
        }
        if (process is null)
        {
            throw new ScanEngineException("helper-start-failed", "Le composant d’analyse MFT n’a pas pu démarrer.");
        }

        using (process)
        {
            var connection = pipe.WaitForConnectionAsync(cancellationToken);
            var exit = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connection, exit) == exit && !pipe.IsConnected)
            {
                throw new ScanEngineException("helper-exited", $"Le composant d’analyse MFT s’est arrêté avant la connexion (code {SafeExitCode(process)}).");
            }
            await connection;
            var sinceConnection = Stopwatch.StartNew();
            VerifyClient(pipe, process, helperPath);

            var (session, summary) = await ScanSession.RunAsync(pipe, progress, cancellationToken);
            await Task.WhenAny(process.WaitForExitAsync(CancellationToken.None), Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None));
            if (summary is null)
            {
                throw new ScanEngineException("helper-closed", "Le composant d’analyse MFT a fermé le canal sans résultat final.");
            }
            var result = ScanEngineClient.Complete(session, summary, total);
            var metrics = result.Summary.ClientMetrics with { SinceConnectionMilliseconds = sinceConnection.Elapsed.TotalMilliseconds };
            return result with { Summary = result.Summary with { ClientMetrics = metrics } };
        }
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode.ToString() : "inconnu";
        }
        catch (InvalidOperationException)
        {
            return "inconnu";
        }
    }

    /// <summary>Le client du canal doit être exactement le helper lancé par cette analyse.</summary>
    private static void VerifyClient(NamedPipeServerStream pipe, Process process, string expectedPath)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId) || clientId != (uint)process.Id)
        {
            pipe.Disconnect();
            throw new ScanEngineException("helper-identity", "Un processus inattendu s’est connecté au canal d’analyse ; analyse MFT abandonnée.");
        }
        var image = QueryImagePath(clientId);
        if (image is null || !string.Equals(Path.GetFullPath(image), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
        {
            pipe.Disconnect();
            throw new ScanEngineException("helper-identity", "L’identité du composant d’analyse MFT n’a pas pu être vérifiée ; analyse abandonnée.");
        }
    }

    private static string? QueryImagePath(uint processId)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// Choix de la méthode : MFT (UAC) pour la racine d'un volume NTFS, repli automatique sur le
/// scan classique en cas de refus ou d'échec, la cause étant conservée dans le résumé.
/// </summary>
public sealed class StorageScanner
{
    private readonly ScanEngineClient _classic = new();
    private readonly MftScanClient _mft = new();

    public async Task<ScanResult> ScanAsync(string target, bool preferMft, IProgress<ScanUpdate>? progress, CancellationToken cancellationToken)
    {
        string? notice = null;
        if (preferMft && MftScanClient.CanScan(target, out var letter))
        {
            try
            {
                return await _mft.ScanVolumeAsync(letter, progress, cancellationToken);
            }
            catch (ScanEngineException exception) when (!cancellationToken.IsCancellationRequested)
            {
                notice = exception.Code == "uac-declined"
                    ? "Analyse rapide non autorisée (UAC refusée) : analyse standard utilisée."
                    : $"Analyse rapide indisponible ({exception.Message}) : analyse standard utilisée.";
                progress?.Report(new ScanWarning(target, notice));
            }
        }
        var result = await _classic.ScanAsync(target, progress, cancellationToken);
        return notice is null ? result : result with { Summary = result.Summary with { Notice = notice } };
    }
}
