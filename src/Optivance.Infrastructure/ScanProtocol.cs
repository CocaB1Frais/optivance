using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Optivance.Core;

namespace Optivance.Infrastructure;

/// <summary>Erreur signalée par un moteur, avec un code machine (ex. « access-denied », « uac-declined »).</summary>
public sealed class ScanEngineException(string code, string message) : IOException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Lecture des lignes du canal sans conversion en chaîne. La mémoire renvoyée n'est valide
/// que jusqu'à l'appel suivant. La taille d'une ligne est bornée (protocole D-10).
/// </summary>
internal sealed class PipeLineReader(Stream stream)
{
    private const int MaxLineBytes = 64 * 1024 * 1024;
    private byte[] _buffer = new byte[1 << 20];
    private int _start;
    private int _end;

    public async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newline = _buffer.AsSpan(_start, _end - _start).IndexOf((byte)'\n');
            if (newline >= 0)
            {
                var line = new ReadOnlyMemory<byte>(_buffer, _start, newline);
                _start += newline + 1;
                return line;
            }

            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            if (_end == _buffer.Length)
            {
                if (_buffer.Length >= MaxLineBytes)
                {
                    throw new InvalidDataException("Message du moteur trop volumineux.");
                }
                Array.Resize(ref _buffer, Math.Min(MaxLineBytes, _buffer.Length * 2));
            }

            var read = await stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken);
            if (read == 0)
            {
                if (_end == _start)
                {
                    return null;
                }
                var rest = new ReadOnlyMemory<byte>(_buffer, _start, _end - _start);
                _start = _end;
                return rest;
            }
            _end += read;
        }
    }
}

/// <summary>Interprétation des messages du protocole v3 (moteur classique et helper MFT).</summary>
internal sealed class ScanSession
{
    private static readonly byte[] ResultsPrefix = Encoding.ASCII.GetBytes("{\"v\":3,\"type\":\"results\",\"start\":");

    public List<ScanEntry> Entries { get; } = new(1 << 16);
    public long ResultBatches { get; private set; }
    public long ParseTicks { get; private set; }
    public string Method { get; private set; } = ScanMethods.Classic;
    public List<ScanWarning> Warnings { get; } = [];

    /// <summary>Renvoie le résumé final quand le message est terminal.</summary>
    public ScanSummary? Process(ReadOnlyMemory<byte> line, IProgress<ScanUpdate>? progress)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (line.Span.StartsWith(ResultsPrefix))
            {
                ReadResults(line.Span);
                return null;
            }
            return ProcessDocument(line, progress);
        }
        finally
        {
            ParseTicks += Stopwatch.GetTimestamp() - started;
        }
    }

    private ScanSummary? ProcessDocument(ReadOnlyMemory<byte> line, IProgress<ScanUpdate>? progress)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("v", out var version) || version.GetInt32() != 3)
        {
            throw new InvalidDataException("Version de protocole du moteur inconnue (v3 attendue).");
        }

        var type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "hello":
                if (root.GetProperty("protocolVersion").GetInt32() != 3)
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
                var warning = new ScanWarning(
                    root.GetProperty("path").GetString() ?? string.Empty,
                    root.GetProperty("message").GetString() ?? "Avertissement sans détail.");
                Warnings.Add(warning);
                progress?.Report(warning);
                return null;
            case "completed":
            case "interrupted":
                var method = root.TryGetProperty("mftMethod", out var mftMethod) ? mftMethod.GetString() ?? Method : Method;
                return new ScanSummary(
                    type == "completed" ? ScanState.Completed : ScanState.Interrupted,
                    root.GetProperty("files").GetInt64(),
                    root.GetProperty("directories").GetInt64(),
                    root.GetProperty("warnings").GetInt64(),
                    root.GetProperty("uniqueLogicalBytes").GetUInt64(),
                    root.GetProperty("uniqueAllocatedBytes").GetUInt64(),
                    TimeSpan.Zero,
                    ParseEngineMetrics(root.GetProperty("profile")),
                    new ScanClientMetrics(0, 0, 0, Entries.Count, ResultBatches),
                    method,
                    root.TryGetProperty("partial", out var partial) && partial.GetBoolean());
            case "error":
                var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() ?? "engine-error" : "engine-error";
                throw new ScanEngineException(code, root.GetProperty("message").GetString() ?? "Le moteur a signalé une erreur.");
            default:
                throw new InvalidDataException($"Message du protocole inconnu : {type ?? "(absent)"}.");
        }
    }

    /// <summary>
    /// Lot compact : <c>{"v":3,"type":"results","start":N,"entries":[[parent,childStart,childCount,dir,name,logical,allocated,dup,modified],…]}</c>.
    /// </summary>
    private void ReadResults(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 8 });
        Expect(ref reader, JsonTokenType.StartObject);
        ExpectProperty(ref reader, "v");
        Expect(ref reader, JsonTokenType.Number);
        ExpectProperty(ref reader, "type");
        Expect(ref reader, JsonTokenType.String);
        ExpectProperty(ref reader, "start");
        Expect(ref reader, JsonTokenType.Number);
        if (reader.GetInt32() != Entries.Count)
        {
            throw new InvalidDataException("Lot de résultats hors séquence.");
        }
        ExpectProperty(ref reader, "entries");
        Expect(ref reader, JsonTokenType.StartArray);
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            var id = Entries.Count;
            var parent = ReadInt32(ref reader);
            var childStart = ReadInt32(ref reader);
            var childCount = ReadInt32(ref reader);
            var isDirectory = ReadInt32(ref reader) != 0;
            Expect(ref reader, JsonTokenType.String);
            var name = reader.GetString() ?? string.Empty;
            var logical = ReadUInt64(ref reader);
            var allocated = ReadUInt64(ref reader);
            var duplicate = ReadInt32(ref reader) != 0;
            var modified = (long)ReadUInt64(ref reader);
            Expect(ref reader, JsonTokenType.EndArray);
            if (parent >= id || parent < -1 || childStart < 0 || childCount < 0 || (!isDirectory && childCount != 0))
            {
                throw new InvalidDataException("Le moteur a envoyé un index d’analyse incohérent.");
            }
            Entries.Add(new ScanEntry(id, parent, childStart, childCount, isDirectory, name, logical, allocated, duplicate, modified));
        }
        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new InvalidDataException("Lot de résultats mal formé.");
        }
        ResultBatches++;
    }

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType type)
    {
        if (!reader.Read() || reader.TokenType != type)
        {
            throw new InvalidDataException("Lot de résultats mal formé.");
        }
    }

    private static void ExpectProperty(ref Utf8JsonReader reader, string name)
    {
        Expect(ref reader, JsonTokenType.PropertyName);
        if (!reader.ValueTextEquals(name))
        {
            throw new InvalidDataException("Lot de résultats mal formé.");
        }
    }

    private static int ReadInt32(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.GetInt32();
    }

    private static ulong ReadUInt64(ref Utf8JsonReader reader)
    {
        Expect(ref reader, JsonTokenType.Number);
        return reader.GetUInt64();
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

    public static void ValidateIndex(IReadOnlyList<ScanEntry> entries, bool requireCompleteRanges)
    {
        if (entries.Count == 0 || !entries[0].IsDirectory || entries[0].ParentId != -1)
        {
            throw new InvalidDataException("Le moteur n’a pas fourni la racine de l’index.");
        }
        foreach (var entry in entries)
        {
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

    /// <summary>Boucle de lecture commune ; envoie l'annulation au moteur si demandé.</summary>
    public static async Task<(ScanSession Session, ScanSummary? Summary)> RunAsync(
        Stream pipe,
        IProgress<ScanUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var session = new ScanSession();
        var reader = new PipeLineReader(pipe);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true) { AutoFlush = true };
        using var cancelRegistration = cancellationToken.Register(() => _ = SendCancelAsync(writer));
        while (await reader.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (line.IsEmpty)
            {
                continue;
            }
            if (session.Process(line, progress) is { } summary)
            {
                return (session, summary);
            }
        }
        return (session, null);
    }

    private static async Task SendCancelAsync(StreamWriter writer)
    {
        try
        {
            await writer.WriteLineAsync("{\"v\":3,\"type\":\"cancel\"}");
        }
        catch (IOException)
        {
            // Le moteur peut déjà avoir terminé.
        }
        catch (ObjectDisposedException)
        {
            // L'analyse peut s'être achevée au même moment que l'annulation.
        }
    }
}
