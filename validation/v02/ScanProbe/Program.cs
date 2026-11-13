using System.Globalization;
using Optivance.Core;
using Optivance.Infrastructure;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine("Usage: ScanProbe.exe <scan-root> [--cancel-after-ms <milliseconds>] | ScanProbe.exe --mft <lettre>");
    return 64;
}

// Mode MFT : lance le helper avec demande UAC (lecture seule), mÃ©triques uniquement.
char? mftLetter = args.Length == 2 && args[0] == "--mft" && args[1].Length >= 1 && char.IsAsciiLetter(args[1][0]) ? args[1][0] : null;
var target = mftLetter is { } volumeLetter ? $"{volumeLetter}:\\" : Path.GetFullPath(args[0]);
if (mftLetter is not null)
{
    args = [target];
}
int? cancelAfterMilliseconds = null;
if (args.Length > 1)
{
    if (args.Length != 3 || args[1] != "--cancel-after-ms" ||
        !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDelay) || parsedDelay <= 0)
    {
        Console.Error.WriteLine("--cancel-after-ms requires a positive integer.");
        return 64;
    }
    cancelAfterMilliseconds = parsedDelay;
}

var latestProgress = new ProgressSnapshot();
var progress = new Progress<ScanUpdate>(update =>
{
    if (update is ScanProgress scanProgress)
    {
        latestProgress = new ProgressSnapshot(scanProgress.VisitedFiles, scanProgress.VisitedDirectories);
    }
});

try
{
    using var cancellation = new CancellationTokenSource();
    if (cancelAfterMilliseconds is { } delay)
    {
        cancellation.CancelAfter(delay);
    }
    var result = mftLetter is { } letter
        ? await new MftScanClient().ScanVolumeAsync(letter, progress, cancellation.Token)
        : await new ScanEngineClient().ScanAsync(target, progress, cancellation.Token);
    var summary = result.Summary;
    Console.WriteLine($"method={summary.Method}");
    Console.WriteLine($"partial={summary.IsPartial}");
    var engine = summary.EngineMetrics;
    var client = summary.ClientMetrics;
    var culture = CultureInfo.InvariantCulture;
    Console.WriteLine($"state={summary.State}");
    Console.WriteLine($"files={summary.Files}");
    Console.WriteLine($"directories={summary.Directories}");
    Console.WriteLine($"warnings={summary.Warnings}");
    Console.WriteLine($"uniqueLogicalBytes={summary.UniqueLogicalBytes}");
    Console.WriteLine($"uniqueAllocatedBytes={summary.UniqueAllocatedBytes}");
    Console.WriteLine($"totalMs={summary.Elapsed.TotalMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"sinceConnectionMs={client.SinceConnectionMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"engineTotalMs={engine.TotalMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"enumerationMs={engine.EnumerationMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"sortingMs={engine.SortingMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"metadataMs={engine.MetadataMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"fileInformationMs={engine.FileInformationMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"aggregationMs={engine.AggregationMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"serializationMs={engine.SerializationMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"pipeWriteMs={engine.PipeWriteMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"metadataLookups={engine.MetadataLookups}");
    Console.WriteLine($"fileInformationLookups={engine.FileInformationLookups}");
    Console.WriteLine($"entriesSent={engine.EntriesSent}");
    Console.WriteLine($"resultBatches={engine.ResultBatches}");
    Console.WriteLine($"bytesSent={engine.BytesSent}");
    Console.WriteLine($"jsonProcessingMs={client.JsonProcessingMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"indexValidationMs={client.IndexValidationMilliseconds.ToString("F3", culture)}");
    Console.WriteLine($"entriesReceived={client.EntriesReceived}");
    Console.WriteLine($"clientResultBatches={client.ResultBatches}");
    Console.WriteLine($"lastProgressFiles={latestProgress.Files}");
    Console.WriteLine($"lastProgressDirectories={latestProgress.Directories}");
    Console.WriteLine($"cancelRequested={cancelAfterMilliseconds.HasValue}");
    return summary.State == ScanState.Completed || (cancelAfterMilliseconds.HasValue && summary.State == ScanState.Interrupted) ? 0 : 1;
}
catch (OperationCanceledException) when (cancelAfterMilliseconds.HasValue)
{
    Console.WriteLine("state=cancelled-before-result");
    Console.WriteLine("cancelRequested=true");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"scan failed: {exception}");
    return 2;
}

internal sealed record ProgressSnapshot(long Files = 0, long Directories = 0);
