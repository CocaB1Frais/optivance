namespace Optivance.Core;

public enum ScanState
{
    Completed,
    Interrupted,
    Failed
}

public abstract record ScanUpdate;

public sealed record ScanProgress(long VisitedFiles, long VisitedDirectories, ulong UniqueLogicalBytes, TimeSpan Elapsed) : ScanUpdate;

public sealed record ScanWarning(string Path, string Message) : ScanUpdate;

/// <summary>
/// One row in the compact, flat scan index. Directory children occupy a contiguous ID range.
/// Paths are reconstructed on demand from the root and parent IDs rather than repeated in IPC.
/// For a directory, the byte counts are the unique totals of its subtree.
/// <see cref="Modified"/> is a UTC FILETIME (0 when unknown).
/// </summary>
public readonly record struct ScanEntry(
    int Id,
    int ParentId,
    int ChildStart,
    int ChildCount,
    bool IsDirectory,
    string Name,
    ulong LogicalBytes,
    ulong AllocatedBytes,
    bool IsHardLinkDuplicate,
    long Modified = 0);

/// <summary>Méthode d'analyse réellement utilisée.</summary>
public static class ScanMethods
{
    public const string Classic = "classic";
    public const string MftRaw = "mft-raw";
    public const string MftEnum = "mft-enum";
}

public sealed record ScanEngineMetrics(
    double TotalMilliseconds,
    double EnumerationMilliseconds,
    double SortingMilliseconds,
    double MetadataMilliseconds,
    double FileInformationMilliseconds,
    double AggregationMilliseconds,
    double SerializationMilliseconds,
    double PipeWriteMilliseconds,
    long MetadataLookups,
    long FileInformationLookups,
    long EntriesSent,
    long ResultBatches,
    long BytesSent);

public sealed record ScanClientMetrics(
    double TotalMilliseconds,
    double JsonProcessingMilliseconds,
    double IndexValidationMilliseconds,
    long EntriesReceived,
    long ResultBatches);

public sealed record ScanSummary(
    ScanState State,
    long Files,
    long Directories,
    long Warnings,
    ulong UniqueLogicalBytes,
    ulong UniqueAllocatedBytes,
    TimeSpan Elapsed,
    ScanEngineMetrics EngineMetrics,
    ScanClientMetrics ClientMetrics,
    string Method = ScanMethods.Classic,
    bool IsPartial = false,
    string? Notice = null);

public sealed record ScanResult(ScanSummary Summary, IReadOnlyList<ScanEntry> Entries);
