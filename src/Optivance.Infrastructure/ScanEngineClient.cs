using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Optivance.Core;

namespace Optivance.Infrastructure;

public sealed class ScanEngineClient
{
    public async Task<ScanResult> ScanAsync(
        string target,
        IProgress<ScanUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var workerPath = Path.Combine(AppContext.BaseDirectory, "optivance-engine.exe");
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("Le moteur Rust est absent du dossier de l’application.", workerPath);
        }

        var total = Stopwatch.StartNew();
        var pipeName = $"optivance-scan-v2-{Guid.NewGuid():N}";
        await using var pipe = SecurePipeServer.CreateCurrentUserOnly(pipeName);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.ArgumentList.Add("--pipe-name");
        process.StartInfo.ArgumentList.Add(pipeName);
        process.StartInfo.ArgumentList.Add("--target");
        process.StartInfo.ArgumentList.Add(Path.GetFullPath(target));

        var started = false;
        var entries = new List<ScanEntry>();
        long jsonProcessingTicks = 0;
        var resultBatches = 0L;
        ScanSummary? finalSummary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                throw new InvalidOperationException("Le moteur Rust n’a pas pu démarrer.");
            }

            started = true;
            var connection = pipe.WaitForConnectionAsync(cancellationToken);
            var workerExit = process.WaitForExitAsync();
            if (await Task.WhenAny(connection, workerExit) == workerExit && !pipe.IsConnected)
            {
                var diagnostic = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Le moteur Rust s’est arrêté avant la connexion (code {process.ExitCode}) : {diagnostic.Trim()}");
            }

            await connection;
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 64 * 1024, leaveOpen: true) { AutoFlush = true };
            using var cancelRegistration = cancellationToken.Register(() => _ = SendCancelAsync(writer));

            while (true)
            {
                var line = await reader.ReadLineAsync(CancellationToken.None);
                if (line is null)
                {
                    break;
                }

                var parseStarted = Stopwatch.GetTimestamp();
                var terminal = ProcessMessage(line, progress, entries, ref resultBatches);
                jsonProcessingTicks += Stopwatch.GetTimestamp() - parseStarted;

                if (terminal is not null)
                {
                    finalSummary = terminal;
                    break;
                }
            }

            await process.WaitForExitAsync();
            if (finalSummary is null)
            {
                var diagnostic = await process.StandardError.ReadToEndAsync();
                throw new EndOfStreamException($"Le moteur Rust a fermé le canal sans résultat final (code {process.ExitCode}) : {diagnostic.Trim()}");
            }

            if (process.ExitCode != 0)
            {
                var diagnostic = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Le moteur Rust a quitté avec le code {process.ExitCode} : {diagnostic.Trim()}");
            }

            var validation = Stopwatch.StartNew();
            ValidateIndex(entries, finalSummary.State != ScanState.Interrupted);
            validation.Stop();
            total.Stop();
            var clientMetrics = new ScanClientMetrics(
                total.Elapsed.TotalMilliseconds,
                Stopwatch.GetElapsedTime(0, jsonProcessingTicks).TotalMilliseconds,
                validation.Elapsed.TotalMilliseconds,
                entries.Count,
                resultBatches);
            var summary = finalSummary with
            {
                Elapsed = total.Elapsed,
                ClientMetrics = clientMetrics
            };
            return new ScanResult(summary, entries);
        }
        catch
        {
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (InvalidOperationException)
                {
                    // Le processus peut quitter entre le contrôle et Kill.
                }
            }

            throw;
        }
    }

    private static async Task SendCancelAsync(StreamWriter writer)
    {
        try
        {
            await writer.WriteLineAsync("{\"v\":2,\"type\":\"cancel\"}");
            await writer.FlushAsync();
        }
        catch (IOException)
        {
            // Le worker peut déjà avoir terminé.
        }
        catch (ObjectDisposedException)
        {
            // Le scan peut s’être achevé au même moment que l’annulation.
        }
    }

    private static ScanSummary? ProcessMessage(
        string line,
        IProgress<ScanUpdate>? progress,
        List<ScanEntry> entries,
        ref long resultBatches)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("v", out var version) || version.GetInt32() != 2)
        {
            throw new InvalidDataException("Version de protocole Named Pipe inconnue.");
        }

        var type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "hello":
                if (root.GetProperty("protocolVersion").GetInt32() != 2)
                {
                    throw new InvalidDataException("Version de protocole du moteur incompatible.");
                }
                return null;
            case "progress":
                progress?.Report(new ScanProgress(
                    root.GetProperty("visitedFiles").GetInt64(),
                    root.GetProperty("visitedDirectories").GetInt64(),
                    root.GetProperty("uniqueLogicalBytes").GetUInt64(),
                    TimeSpan.FromMilliseconds(root.GetProperty("elapsedMs").GetDouble())));
                return null;
            case "warning":
                progress?.Report(new ScanWarning(
                    root.GetProperty("path").GetString() ?? string.Empty,
                    root.GetProperty("message").GetString() ?? "Avertissement sans détail."));
                return null;
            case "results":
                foreach (var item in root.GetProperty("entries").EnumerateArray())
                {
                    var entry = new ScanEntry(
                        item.GetProperty("id").GetInt32(),
                        item.GetProperty("parentId").GetInt32(),
                        item.GetProperty("childStart").GetInt32(),
                        item.GetProperty("childCount").GetInt32(),
                        item.GetProperty("kind").GetString() == "directory",
                        item.GetProperty("name").GetString() ?? string.Empty,
                        item.GetProperty("logicalBytes").GetUInt64(),
                        item.GetProperty("allocatedBytes").GetUInt64(),
                        item.GetProperty("hardLinkDuplicate").GetBoolean());
                    if (entry.Id != entries.Count || entry.ParentId >= entry.Id || entry.ParentId < -1 || entry.ChildStart < 0 || entry.ChildCount < 0)
                    {
                        throw new InvalidDataException("Le moteur a envoyé un index d’analyse incohérent.");
                    }
                    entries.Add(entry);
                }
                resultBatches++;
                return null;
            case "completed":
            case "interrupted":
                return new ScanSummary(
                    type == "completed" ? ScanState.Completed : ScanState.Interrupted,
                    root.GetProperty("files").GetInt64(),
                    root.GetProperty("directories").GetInt64(),
                    root.GetProperty("warnings").GetInt64(),
                    root.GetProperty("uniqueLogicalBytes").GetUInt64(),
                    root.GetProperty("uniqueAllocatedBytes").GetUInt64(),
                    TimeSpan.Zero,
                    ParseEngineMetrics(root.GetProperty("profile")),
                    new ScanClientMetrics(0, 0, 0, entries.Count, resultBatches));
            case "error":
                throw new IOException(root.GetProperty("message").GetString() ?? "Le moteur Rust a signalé une erreur.");
            default:
                throw new InvalidDataException($"Message du protocole inconnu : {type ?? "(absent)"}.");
        }
    }

    private static ScanEngineMetrics ParseEngineMetrics(JsonElement profile) => new(
        profile.GetProperty("totalMs").GetDouble(),
        profile.GetProperty("enumerationMs").GetDouble(),
        profile.GetProperty("sortingMs").GetDouble(),
        profile.GetProperty("metadataMs").GetDouble(),
        profile.GetProperty("fileInformationMs").GetDouble(),
        profile.GetProperty("aggregationMs").GetDouble(),
        profile.GetProperty("serializationMs").GetDouble(),
        profile.GetProperty("pipeWriteMs").GetDouble(),
        profile.GetProperty("metadataLookups").GetInt64(),
        profile.GetProperty("fileInformationLookups").GetInt64(),
        profile.GetProperty("entriesSent").GetInt64(),
        profile.GetProperty("resultBatches").GetInt64(),
        profile.GetProperty("bytesSent").GetInt64());

    private static void ValidateIndex(IReadOnlyList<ScanEntry> entries, bool requireCompleteRanges)
    {
        if (entries.Count == 0 || entries[0].Id != 0 || !entries[0].IsDirectory || entries[0].ParentId != -1)
        {
            throw new InvalidDataException("Le moteur n’a pas fourni la racine de l’index.");
        }
        foreach (var entry in entries)
        {
            if (!entry.IsDirectory && entry.ChildCount != 0)
            {
                throw new InvalidDataException($"Le fichier {entry.Id} déclare des enfants.");
            }
            if (requireCompleteRanges && (long)entry.ChildStart + entry.ChildCount > entries.Count)
            {
                throw new InvalidDataException($"La plage des enfants de l’entrée {entry.Id} est invalide.");
            }
            var end = (int)Math.Min(entries.Count, (long)entry.ChildStart + entry.ChildCount);
            for (var childId = entry.ChildStart; childId < end; childId++)
            {
                if (entries[childId].ParentId != entry.Id)
                {
                    throw new InvalidDataException($"La relation parent/enfant de l’entrée {childId} est invalide.");
                }
            }
        }
    }
}
