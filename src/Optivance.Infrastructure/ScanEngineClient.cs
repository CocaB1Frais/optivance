using System.Diagnostics;
using Optivance.Core;

namespace Optivance.Infrastructure;

/// <summary>Scan classique : moteur Rust non élevé, parcours des dossiers.</summary>
public sealed class ScanEngineClient
{
    public async Task<ScanResult> ScanAsync(
        string target,
        IProgress<ScanUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var workerPath = Path.Combine(MftScanClient.EngineFolder, "optivance-engine.exe");
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("Le moteur Rust est absent du dossier de l’application.", workerPath);
        }

        var total = Stopwatch.StartNew();
        var pipeName = $"optivance-scan-v3-{Guid.NewGuid():N}";
        await using var pipe = SecurePipeServer.CreateCurrentUserOnly(pipeName);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = MftScanClient.EngineFolder
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add("--pipe-name");
        process.StartInfo.ArgumentList.Add(pipeName);
        process.StartInfo.ArgumentList.Add("--target");
        process.StartInfo.ArgumentList.Add(Path.GetFullPath(target));

        var started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                throw new InvalidOperationException("Le moteur Rust n’a pas pu démarrer.");
            }
            started = true;

            var connection = pipe.WaitForConnectionAsync(cancellationToken);
            var workerExit = process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connection, workerExit) == workerExit && !pipe.IsConnected)
            {
                var diagnostic = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Le moteur Rust s’est arrêté avant la connexion (code {process.ExitCode}) : {diagnostic.Trim()}");
            }
            await connection;

            var (session, summary) = await ScanSession.RunAsync(pipe, progress, cancellationToken);
            await process.WaitForExitAsync(CancellationToken.None);
            if (summary is null)
            {
                var diagnostic = await process.StandardError.ReadToEndAsync();
                throw new EndOfStreamException($"Le moteur Rust a fermé le canal sans résultat final (code {process.ExitCode}) : {diagnostic.Trim()}");
            }
            if (process.ExitCode != 0)
            {
                var diagnostic = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Le moteur Rust a quitté avec le code {process.ExitCode} : {diagnostic.Trim()}");
            }
            return Complete(session, summary, total);
        }
        catch
        {
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // Le processus peut quitter entre le contrôle et Kill.
                }
            }
            throw;
        }
    }

    internal static ScanResult Complete(ScanSession session, ScanSummary summary, Stopwatch total)
    {
        var validation = Stopwatch.StartNew();
        ScanSession.ValidateIndex(session.Entries, summary.State != ScanState.Interrupted);
        validation.Stop();
        total.Stop();
        var clientMetrics = new ScanClientMetrics(
            total.Elapsed.TotalMilliseconds,
            Stopwatch.GetElapsedTime(0, session.ParseTicks).TotalMilliseconds,
            validation.Elapsed.TotalMilliseconds,
            session.Entries.Count,
            session.ResultBatches);
        return new ScanResult(summary with { Elapsed = total.Elapsed, ClientMetrics = clientMetrics }, session.Entries);
    }
}
