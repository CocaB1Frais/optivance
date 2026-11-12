using Optivance.Core;
using Optivance.Infrastructure;

var target = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "engine", "src"));

var progress = new Progress<ScanUpdate>(update =>
{
    if (update is ScanProgress value)
    {
        Console.WriteLine($"progress: directories={value.VisitedDirectories}; files={value.VisitedFiles}; uniqueLogicalBytes={value.UniqueLogicalBytes}");
    }
    else if (update is ScanWarning warning)
    {
        Console.WriteLine($"warning: {warning.Path}: {warning.Message}");
    }
    else if (update is ScanEntries batch)
    {
        Console.WriteLine($"results: {batch.Entries.Count} entries");
    }
});

try
{
    Console.WriteLine($"scan target: {target}");
    var summary = await new ScanEngineClient().ScanAsync(target, progress, CancellationToken.None);
    Console.WriteLine($"state={summary.State}; directories={summary.Directories}; files={summary.Files}; warnings={summary.Warnings}; logical={summary.UniqueLogicalBytes}; allocated={summary.UniqueAllocatedBytes}; elapsedMs={summary.Elapsed.TotalMilliseconds:N0}");
    return summary.State == ScanState.Completed ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"scan failed: {exception.GetType().Name}: {exception.Message}");
    return 2;
}
