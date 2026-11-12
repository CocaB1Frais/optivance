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
    bool IsHardLinkDuplicate);

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
    ScanClientMetrics ClientMetrics);

public sealed record ScanResult(ScanSummary Summary, IReadOnlyList<ScanEntry> Entries);
