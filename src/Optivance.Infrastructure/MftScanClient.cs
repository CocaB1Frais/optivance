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
    internal static void VerifyClient(NamedPipeServerStream pipe, Process process, string expectedPath)
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
/// Composant d'analyse MFT de session (choix du propriétaire du 13/11/2026) : une seule
/// demande UAC au lancement d'OPTIVANCE ; le helper élevé reste connecté et exécute les
/// analyses demandées jusqu'à la fermeture de l'application. L'interface reste non élevée,
/// aucun service n'est installé, le helper n'accepte que « analyser un volume », « annuler »
/// et « quitter », et son identité (PID + image) est vérifiée à la connexion.
/// </summary>
public sealed class MftSession : IAsyncDisposable
{
    private const int ErrorCancelled = 1223;
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _process;
    private readonly PipeLineReader _reader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _broken;

    private MftSession(NamedPipeServerStream pipe, Process process)
    {
        _pipe = pipe;
        _process = process;
        _reader = new PipeLineReader(pipe);
    }

    public bool IsActive => !_broken && _pipe.IsConnected && !_process.HasExited;

    /// <summary>Demande l'élévation (UAC) et attend la connexion du helper.</summary>
    public static async Task<MftSession> StartAsync(CancellationToken cancellationToken)
    {
        var helperPath = MftScanClient.HelperPath;
        if (!File.Exists(helperPath))
        {
            throw new ScanEngineException("helper-missing", "Le composant d’analyse rapide est absent du dossier de l’application.");
        }
        var pipeName = $"optivance-mft-{Guid.NewGuid():N}";
        var pipe = SecurePipeServer.CreateCurrentUserOnly(pipeName);
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = $"--pipe-name {pipeName} --session",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            await pipe.DisposeAsync();
            throw new ScanEngineException("uac-declined", "Autorisation administrateur refusée : l’analyse rapide est désactivée pour cette session.");
        }
        if (process is null)
        {
            await pipe.DisposeAsync();
            throw new ScanEngineException("helper-start-failed", "Le composant d’analyse rapide n’a pas pu démarrer.");
        }

        try
        {
            var connection = pipe.WaitForConnectionAsync(cancellationToken);
            var exit = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connection, exit) == exit && !pipe.IsConnected)
            {
                throw new ScanEngineException("helper-exited", "Le composant d’analyse rapide s’est arrêté avant la connexion.");
            }
            await connection;
            MftScanClient.VerifyClient(pipe, process, helperPath);
            var session = new MftSession(pipe, process);
            var hello = await session._reader.ReadLineAsync(cancellationToken);
            if (hello is not { } line || !line.Span.StartsWith("{\"v\":3,\"type\":\"hello\""u8))
            {
                throw new ScanEngineException("helper-protocol", "Le composant d’analyse rapide ne parle pas le protocole attendu.");
            }
            return session;
        }
        catch
        {
            await pipe.DisposeAsync();
            process.Dispose();
            throw;
        }
    }

    public async Task<ScanResult> ScanVolumeAsync(char letter, IProgress<ScanUpdate>? progress, CancellationToken cancellationToken)
    {
        if (!char.IsAsciiLetter(letter))
        {
            throw new ArgumentException("Lettre de volume invalide.", nameof(letter));
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsActive)
            {
                throw new ScanEngineException("session-closed", "Le composant d’analyse rapide n’est plus disponible.");
            }
            var total = Stopwatch.StartNew();
            var command = Encoding.ASCII.GetBytes($"{{\"v\":3,\"type\":\"scan\",\"volume\":\"{char.ToUpperInvariant(letter)}\"}}\n");
            try
            {
                await _pipe.WriteAsync(command, cancellationToken);
                await _pipe.FlushAsync(cancellationToken);
                var (session, summary) = await ScanSession.RunUntilTerminalAsync(_reader, _pipe, progress, cancellationToken);
                if (summary is null)
                {
                    _broken = true;
                    throw new ScanEngineException("session-closed", "Le composant d’analyse rapide a fermé le canal.");
                }
                var result = ScanEngineClient.Complete(session, summary, total);
                var metrics = result.Summary.ClientMetrics with { SinceConnectionMilliseconds = total.Elapsed.TotalMilliseconds };
                return result with { Summary = result.Summary with { ClientMetrics = metrics } };
            }
            catch (Exception exception) when (exception is IOException and not ScanEngineException or InvalidDataException or ObjectDisposedException)
            {
                // Canal dans un état inconnu : la session n'est plus réutilisée.
                _broken = true;
                throw new ScanEngineException("session-closed", $"Le composant d’analyse rapide ne répond plus ({exception.Message}).");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Nettoyage approfondi d'une catégorie du catalogue (identifiant seul, aucun chemin) :
    /// aperçu si <paramref name="run"/> est faux, suppression sinon.
    /// </summary>
    public async Task<DeepCleanReport> CleanAsync(string categoryId, bool run, CancellationToken cancellationToken)
    {
        if (CleaningCatalog.Find(categoryId) is not { Level: CleaningLevel.Deep })
        {
            throw new ArgumentException("Catégorie de nettoyage approfondi inconnue.", nameof(categoryId));
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsActive)
            {
                throw new ScanEngineException("session-closed", "Le composant administrateur n’est plus disponible.");
            }
            var command = Encoding.ASCII.GetBytes($"{{\"v\":3,\"type\":\"{(run ? "clean-run" : "clean-analyze")}\",\"category\":\"{categoryId}\"}}\n");
            try
            {
                await _pipe.WriteAsync(command, CancellationToken.None);
                await _pipe.FlushAsync(CancellationToken.None);
                using var registration = cancellationToken.Register(() =>
                {
                    try
                    {
                        _pipe.Write("{\"v\":3,\"type\":\"cancel\"}\n"u8);
                        _pipe.Flush();
                    }
                    catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                    {
                    }
                });
                while (await _reader.ReadLineAsync(CancellationToken.None) is { } line)
                {
                    if (line.Span.StartsWith("{\"v\":3,\"type\":\"clean\""u8))
                    {
                        return System.Text.Json.JsonSerializer.Deserialize<DeepCleanReport>(line.Span, ReportOptions)
                            ?? throw new InvalidDataException("Réponse de nettoyage vide.");
                    }
                }
                _broken = true;
                throw new ScanEngineException("session-closed", "Le composant administrateur a fermé le canal.");
            }
            catch (Exception exception) when (exception is IOException and not ScanEngineException or InvalidDataException or System.Text.Json.JsonException or ObjectDisposedException)
            {
                _broken = true;
                throw new ScanEngineException("session-closed", $"Le composant administrateur ne répond plus ({exception.Message}).");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions ReportOptions = new() { PropertyNameCaseInsensitive = true };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsActive)
            {
                await _pipe.WriteAsync("{\"v\":3,\"type\":\"quit\"}\n"u8.ToArray());
                await _pipe.FlushAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Le helper peut déjà être arrêté.
        }
        await _pipe.DisposeAsync();
        await Task.WhenAny(_process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(3)));
        _process.Dispose();
        _gate.Dispose();
    }
}

/// <summary>
/// Choix de la méthode : MFT par le composant de session pour la racine d'un volume NTFS,
/// repli automatique sur le scan classique (cause conservée dans le résumé). Aucune demande
/// UAC n'est faite pendant une analyse.
/// </summary>
public sealed class StorageScanner
{
    private readonly ScanEngineClient _classic = new();

    /// <summary>Composant de session actif, ou null (non autorisé, arrêté).</summary>
    public MftSession? Session { get; set; }

    public async Task<ScanResult> ScanAsync(string target, bool preferMft, IProgress<ScanUpdate>? progress, CancellationToken cancellationToken)
    {
        string? notice = null;
        if (preferMft && MftScanClient.CanScan(target, out var letter))
        {
            if (Session is { IsActive: true } session)
            {
                try
                {
                    return await session.ScanVolumeAsync(letter, progress, cancellationToken);
                }
                catch (ScanEngineException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    notice = $"Analyse rapide indisponible ({exception.Message}) : analyse standard utilisée.";
                    progress?.Report(new ScanWarning(target, notice));
                }
            }
            else
            {
                notice = "Analyse rapide non autorisée pour cette session : analyse standard utilisée.";
                progress?.Report(new ScanWarning(target, notice));
            }
        }
        var result = await _classic.ScanAsync(target, progress, cancellationToken);
        return notice is null ? result : result with { Summary = result.Summary with { Notice = notice } };
    }
}
