using System.Globalization;
using Optivance.Core;
using Optivance.Infrastructure;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine("Usage: ScanProbe.exe <scan-root> [--cancel-after-ms <milliseconds>] | ScanProbe.exe --mft <lettre>");
    return 64;
}

// Mode monitoring : deux relevés des capteurs (sans élévation), valeurs affichées telles quelles.
if (args.Length == 1 && args[0] == "--monitor")
{
    var monitor = Optivance.Infrastructure.Monitoring.SystemMonitor.Shared;
    var samples = new List<SystemSnapshot>();
    using (monitor.Subscribe(samples.Add))
    {
        await Task.Delay(2600);
    }
    Console.WriteLine($"cpuName={monitor.CpuName}");
    foreach (var property in typeof(SystemSnapshot).GetProperties())
    {
        Console.WriteLine($"{property.Name}={property.GetValue(samples[^1]) ?? "indisponible"}");
    }
    return 0;
}

// Mode FPS : composant administrateur (UAC), FPS de la fenêtre au premier plan chaque seconde pendant N s,
// et coût processeur du composant pendant la mesure.
if (args.Length is 1 or 2 && args[0] == "--fps")
{
    var seconds = args.Length == 2 && int.TryParse(args[1], out var s) ? s : 45;
    await using var session = await MftSession.StartAsync(CancellationToken.None);
    Console.WriteLine($"session ETW démarrée : {await session.StartFpsAsync()}");
    var helper = System.Diagnostics.Process.GetProcessesByName("optivance-mft-helper").FirstOrDefault();
    var cpuStart = helper?.TotalProcessorTime;
    var clock = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < seconds; i++)
    {
        await Task.Delay(1000);
        var window = NativeForeground.Window();
        NativeForeground.GetWindowThreadProcessId(window, out var pid);
        string name;
        try { using var process = System.Diagnostics.Process.GetProcessById((int)pid); name = process.ProcessName; } catch (ArgumentException) { name = "?"; }
        var fps = await session.QueryFpsAsync((int)pid);
        Console.WriteLine($"{i + 1,3} s  {name,-28} {(fps is { } f ? $"{f:0.0} FPS" : "—")}");
    }
    if (helper is not null && cpuStart is { } start)
    {
        helper.Refresh();
        var used = (helper.TotalProcessorTime - start).TotalSeconds / clock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
        Console.WriteLine($"coût du composant pendant la mesure : {used:0.00} % du processeur, mémoire {helper.PrivateMemorySize64 / 1048576.0:0.0} Mo");
    }
    return 0;
}

// Mode mise à jour : chaîne complète contre la vraie version publiée, sur un fichier de test
// (aucune application réelle remplacée, rien n'est relancé).
if (args.Length == 2 && args[0] == "--update-test")
{
    var folder = Directory.CreateDirectory(args[1]).FullName;
    var testExe = Path.Combine(folder, "OPTIVANCE.exe");
    File.WriteAllText(testExe, "ancienne version factice");
    var release = await UpdateService.CheckAsync(current: new Version(1, 0, 0));
    Console.WriteLine(release is null ? "aucune version plus récente que 1.0.0 (inattendu)" : $"version trouvée : {release.Tag}, {release.ExeSize / 1048576.0:0.0} Mo, empreinte publiée : {release.ShaUrl is not null}");
    if (release is null)
    {
        return 1;
    }
    var progressSteps = 0;
    var downloaded = await UpdateService.DownloadAsync(release, new Progress<double>(_ => progressSteps++));
    Console.WriteLine($"téléchargé et vérifié : {new FileInfo(downloaded).Length / 1048576.0:0.0} Mo ({progressSteps} étapes de progression)");
    UpdateService.Install(downloaded, testExe);
    Console.WriteLine($"remplacé : {new FileInfo(testExe).Length / 1048576.0:0.0} Mo ; ancien conservé pour suppression : {File.Exists(testExe + UpdateService.OldSuffix)}");
    UpdateService.CleanupOld(testExe);
    Console.WriteLine($"ancien supprimé : {!File.Exists(testExe + UpdateService.OldSuffix)}");
    Console.WriteLine($"déjà à jour si l'on est en {release.Tag} : {await UpdateService.CheckAsync(current: release.Version) is null}");
    return 0;
}

// Mode AMD : lecture des capteurs via ADL (atiadlxx.dll du pilote AMD).
if (args.Length == 1 && args[0] == "--amd")
{
    Console.WriteLine(Optivance.Infrastructure.Monitoring.SystemMonitor.ProbeAmd());
    return 0;
}

// Mode doublons : analyse standard d'un dossier puis recherche (diagnostic de FileFinder).
if (args.Length == 2 && args[0] == "--duplicates")
{
    var folder = Path.GetFullPath(args[1]);
    var scan = await new ScanEngineClient().ScanAsync(folder, null, CancellationToken.None);
    var tree = new Optivance.Core.Storage.StorageTree(folder, scan.Entries);
    var filter = new Optivance.Core.Storage.FileFilter(MinimumBytes: 1UL << 20);
    var sameSize = Optivance.Core.Storage.FileFinder.SameSizeGroups(tree, filter);
    Console.WriteLine($"fichiers={tree.Files(0)} candidats(même taille)={sameSize.Count}");
    foreach (var group in sameSize.Take(5))
    {
        foreach (var id in group)
        {
            var path = tree.GetPath(id);
            Console.WriteLine($"  {tree.Size(id)} dup={tree.IsDuplicate(id)} existe={File.Exists(path)} partiel={Optivance.Core.Storage.FileFinder.PartialHash(path, tree.Size(id))?[..20]} {path}");
        }
    }
    var groups = await Optivance.Core.Storage.FileFinder.DuplicatesAsync(tree, filter, null, CancellationToken.None);
    Console.WriteLine($"groupes de doublons={groups.Count}");
    return 0;
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
    if (update is ScanWarning warning)
    {
        Console.WriteLine($"warning={warning.Message}");
    }
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

static class NativeForeground
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    public static IntPtr Window() => GetForegroundWindow();
}
