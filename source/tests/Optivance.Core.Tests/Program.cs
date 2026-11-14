using Optivance.Core;
using Optivance.Core.Storage;

var failures = 0;
var passed = 0;
void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        passed++;
        return;
    }
    failures++;
    Console.WriteLine($"ÉCHEC : {name}{(detail is null ? "" : " — " + detail)}");
}

// Arbre de référence (format du moteur : dossiers = totaux uniques du sous-arbre) :
// 0 R (30) → 1 a/ (10) , 2 z.bin (20)
// 1 a/     → 3 x.txt (10), 4 y.txt (lien secondaire de x, 10, poids 0)
static List<ScanEntry> Sample() =>
[
    new(0, -1, 1, 2, true, "R", 30, 40, false, 100),
    new(1, 0, 3, 2, true, "a", 10, 12, false, 200),
    new(2, 0, 0, 0, false, "z.bin", 20, 28, false, 300),
    new(3, 1, 0, 0, false, "x.txt", 10, 12, false, 400),
    new(4, 1, 0, 0, false, "y.txt", 10, 12, true, 500),
];

{
    var tree = new StorageTree(@"C:\", Sample());
    Check("total unique", tree.TotalBytes == 30, tree.TotalBytes.ToString());
    Check("compteurs racine", tree.Files(0) == 3 && tree.Folders(0) == 1, $"{tree.Files(0)} fichiers, {tree.Folders(0)} dossiers");
    Check("enfants triés par poids", tree.ChildrenBySize(0).SequenceEqual([2, 1]));
    Check("doublon affiché mais sans poids", tree.Size(4) == 10 && tree.Weight(4) == 0);
    Check("chemin", tree.GetPath(3) == @"C:\a\x.txt", tree.GetPath(3));
    Check("ascendance", tree.Ancestry(3).SequenceEqual([0, 1, 3]));
    var txt = tree.Extensions.Single(e => e.Extension == ".txt");
    Check("extension .txt", txt.Files == 2 && txt.Bytes == 10, $"{txt.Files} fichiers, {txt.Bytes} o");
    Check("rang des extensions", tree.Extensions.Single(e => e.Extension == ".bin").Rank == 0 && txt.Rank == 1);
    Check("fichiers par taille", tree.FilesBySize().Take(1).SequenceEqual([2]));
    Check("tri par nom", tree.Children(0, StorageSortKey.Name, false).SequenceEqual([1, 2]));
    Check("tri par date décroissante", tree.Children(1, StorageSortKey.Modified, true).SequenceEqual([4, 3]));

    var freed = tree.Remove(1);
    Check("suppression dossier : taille retirée", freed == 10 && tree.TotalBytes == 20, $"{freed} / {tree.TotalBytes}");
    Check("suppression dossier : compteurs", tree.Files(0) == 1 && tree.Folders(0) == 0);
    Check("suppression dossier : extensions", txt.Files == 0 && txt.Bytes == 0);
    Check("suppression dossier : fichiers listés", tree.FilesBySize().SequenceEqual([2]));
    Check("suppression dossier : enfants", tree.ChildrenBySize(0).SequenceEqual([2]));
    Check("double suppression sans effet", tree.Remove(1) == 0 && tree.TotalBytes == 20);
    Check("racine non supprimable", tree.Remove(0) == 0);
    tree.Remove(2);
    Check("suppression fichier", tree.TotalBytes == 0 && tree.TotalAllocated == 0 && !tree.HasChildren(0));
}

{
    Check("extension simple", StorageTree.ExtensionOf("Photo.JPG") == ".jpg");
    Check("fichier caché sans extension", StorageTree.ExtensionOf(".gitignore") == StorageTree.NoExtension);
    Check("point final", StorageTree.ExtensionOf("nom.") == StorageTree.NoExtension);
    Check("extension trop longue", StorageTree.ExtensionOf("a.abcdefghijklmnopq") == StorageTree.NoExtension);
}

{
    double[] weights = [6, 6, 4, 3, 2, 2, 1];
    var rects = new RectD[weights.Length];
    Squarify.Layout(weights, new RectD(0, 0, 6, 4), rects);
    var area = rects.Sum(rect => rect.Area);
    Check("squarify : surface conservée", Math.Abs(area - 24) < 1e-9, area.ToString());
    Check("squarify : dans les bornes", rects.All(r => r.X >= -1e-9 && r.Y >= -1e-9 && r.Right <= 6 + 1e-9 && r.Bottom <= 4 + 1e-9));
    var proportional = weights.Select((weight, index) => Math.Abs(rects[index].Area - weight) < 1e-9).All(ok => ok);
    Check("squarify : surfaces proportionnelles", proportional);
    var overlap = false;
    for (var i = 0; i < rects.Length; i++)
    {
        for (var j = i + 1; j < rects.Length; j++)
        {
            var w = Math.Min(rects[i].Right, rects[j].Right) - Math.Max(rects[i].X, rects[j].X);
            var h = Math.Min(rects[i].Bottom, rects[j].Bottom) - Math.Max(rects[i].Y, rects[j].Y);
            overlap |= w > 1e-9 && h > 1e-9;
        }
    }
    Check("squarify : aucun chevauchement", !overlap);
}

{
    var tree = new StorageTree(@"C:\", Sample());
    var frame = CushionTreemap.Render(tree, 0, 120, 80, _ => 0x4F8DF5, CancellationToken.None);
    Check("treemap : chaque pixel a un propriétaire", frame.Owners.All(owner => owner >= 0));
    var owners = frame.Owners.Distinct().ToHashSet();
    Check("treemap : fichiers visibles", owners.Contains(2) && owners.Contains(3));
    Check("treemap : lien secondaire absent", !owners.Contains(4));
    var zArea = frame.Owners.Count(owner => owner == 2);
    Check("treemap : surface ~ poids (z = 2/3)", Math.Abs(zArea / (120.0 * 80) - 2.0 / 3) < 0.08, $"{zArea / (120.0 * 80):P1}");
    Check("treemap : rectangles connus", frame.Bounds.ContainsKey(0) && frame.Bounds.ContainsKey(1));
    Check("treemap : test de position", frame.HitTest(-1, 0) == -1 && frame.HitTest(60, 40) >= 0);
}

{
    // Bandeaux de dossiers (style WizTree) : titre au-dessus des enfants, cadre au dossier.
    var tree = new StorageTree(@"C:\", Sample());
    var frame = CushionTreemap.Render(tree, 0, 400, 300, _ => 0x4F8DF5, CancellationToken.None, headerHeight: 15);
    var rootLabel = frame.Labels.FirstOrDefault(label => label.Id == 0);
    var folderLabel = frame.Labels.FirstOrDefault(label => label.Id == 1);
    Check("bandeau : racine titrée", rootLabel.Bounds.Height == 15 && rootLabel.Bounds.Width > 300, rootLabel.ToString());
    Check("bandeau : dossier assez grand titré", folderLabel.Bounds.Height == 15, folderLabel.ToString());
    var header = folderLabel.Bounds;
    var headerOwners = Enumerable.Range((int)header.X + 2, Math.Max(1, (int)header.Width - 4))
        .Select(x => frame.HitTest(x, (int)header.Y + 7)).Distinct().ToList();
    Check("bandeau : appartient au dossier", headerOwners.SequenceEqual([1]), string.Join(",", headerOwners));
    Check("bandeau : enfants sous le titre", frame.Bounds[3].Y >= header.Bottom - 1e-9, frame.Bounds[3].ToString());
    Check("bandeau : aucun titre pour un fichier", frame.Labels.All(label => tree.IsDirectory(label.Id)));
    var small = CushionTreemap.Render(tree, 0, 30, 20, _ => 0x4F8DF5, CancellationToken.None, headerHeight: 15);
    Check("bandeau : trop petit, pas de titre", small.Labels.Count == 0 && small.Owners.All(owner => owner >= 0));
}

{
    Check("format octets", StorageFormat.Bytes(1536) == "1,50 Ko", StorageFormat.Bytes(1536));
    Check("format Go", StorageFormat.Bytes(187_855_987_472) == "175 Go", StorageFormat.Bytes(187_855_987_472));
    Check("format pourcentage", StorageFormat.Percent(0.256) == "26 %" && StorageFormat.Percent(0.0004) == "< 0,1 %", StorageFormat.Percent(0.256));
    Check("format date inconnue", StorageFormat.Date(0) == "—");
}

{
    // Suppression définitive sur des fichiers temporaires (la Corbeille n'est pas utilisée ici).
    var folder = Path.Combine(Path.GetTempPath(), $"optivance-delete-test-{Environment.ProcessId}-{Environment.TickCount64}");
    Directory.CreateDirectory(Path.Combine(folder, "sous"));
    var file = Path.Combine(folder, "fichier.bin");
    File.WriteAllBytes(file, new byte[4096]);
    File.WriteAllBytes(Path.Combine(folder, "sous", "autre.bin"), new byte[10]);
    try
    {
        Check("suppression fichier définitive", Optivance.Infrastructure.ShellOperations.Delete(file, toRecycleBin: false) is null && !File.Exists(file));
        Check("suppression dossier définitive", Optivance.Infrastructure.ShellOperations.Delete(Path.Combine(folder, "sous"), toRecycleBin: false) is null && !Directory.Exists(Path.Combine(folder, "sous")));
        Check("élément absent : erreur signalée", Optivance.Infrastructure.ShellOperations.Delete(file, toRecycleBin: false) is not null);
        Check("racine de volume refusée", Optivance.Infrastructure.ShellOperations.Delete(@"C:\", toRecycleBin: false) is not null);
    }
    finally
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

{
    // Paramètres et historique : fichiers locaux temporaires.
    var folder = Path.Combine(Path.GetTempPath(), $"optivance-settings-test-{Environment.ProcessId}-{Environment.TickCount64}");
    try
    {
        var settingsPath = Path.Combine(folder, "settings.json");
        var settings = new AppSettings { WidgetOpacity = 5, WidgetToggleHotkey = "Ctrl+Shift+F9", CloseToTray = false };
        settings.Save(settingsPath);
        var reloaded = AppSettings.Load(settingsPath);
        Check("paramètres : relus", reloaded.WidgetToggleHotkey == "Ctrl+Shift+F9" && !reloaded.CloseToTray);
        Check("paramètres : bornés", reloaded.WidgetOpacity == 1.0, reloaded.WidgetOpacity.ToString());
        File.WriteAllText(settingsPath, "{ illisible");
        Check("paramètres : fichier illisible → défauts", AppSettings.Load(settingsPath).CloseToTray);

        var history = new HistoryLog(Path.Combine(folder, "history.jsonl"));
        HistoryEntry? notified = null;
        history.Added += entry => notified = entry;
        history.Add(HistoryCategory.Storage, "Corbeille", @"C:\x.bin", "4 Ko");
        history.Add(HistoryCategory.Cleaning, "Nettoyage", "Temporaires", "12 Mo", success: false);
        var entries = history.Load(retentionDays: 30);
        Check("historique : plus récent d'abord", entries.Count == 2 && entries[0].Category == HistoryCategory.Cleaning && !entries[0].Success);
        Check("historique : notification", notified?.Action == "Nettoyage");
        File.AppendAllText(Path.Combine(folder, "history.jsonl"), "pas du json" + Environment.NewLine);
        Check("historique : ligne corrompue ignorée", history.Load(30).Count == 2);
        history.Clear();
        Check("historique : effacé", history.Load(30).Count == 0);
    }
    finally
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

{
    // Monitoring : agrégation des compteurs Windows.
    (string, double)[] engines =
    [
        ("pid_1_luid_0x00000000_0x0000D1A2_phys_0_eng_0_engtype_3D", 30),
        ("pid_2_luid_0x00000000_0x0000D1A2_phys_0_eng_0_engtype_3D", 25),
        ("pid_2_luid_0x00000000_0x0000D1A2_phys_0_eng_1_engtype_Copy", 90),
        ("pid_3_luid_0x00000000_0x0000BEEF_phys_0_eng_0_engtype_3D", 10),
    ];
    Check("GPU : somme 3D de l'adaptateur le plus chargé", MonitoringMath.Gpu3dPercent(engines) == 55, MonitoringMath.Gpu3dPercent(engines)?.ToString());
    Check("GPU : borné à 100 %", MonitoringMath.Gpu3dPercent([("luid_1_phys_0_engtype_3D", 80), ("luid_1_phys_0_engtype_3D", 70)]) == 100);
    Check("GPU : aucun moteur 3D → indisponible", MonitoringMath.Gpu3dPercent([("luid_1_phys_0_engtype_Copy", 5)]) is null);
    Check("thermique : zone la plus chaude", Math.Abs(MonitoringMath.HottestZoneCelsius([300.15, 333.15, 0])!.Value - 60) < 1e-9);
    Check("thermique : valeurs absurdes ignorées", MonitoringMath.HottestZoneCelsius([0, 5000]) is null);
    Check("réseau : boucle locale exclue", MonitoringMath.NetworkTotal([("Intel Ethernet", 100), ("Loopback Pseudo-Interface 1", 999)]) == 100);
}

{
    // Nettoyage : catalogue.
    var ids = CleaningCatalog.All.Select(c => c.Id).ToList();
    Check("nettoyage : identifiants uniques", ids.Distinct().Count() == ids.Count);
    Check("nettoyage : catégories admin = approfondies", CleaningCatalog.All.All(c => c.RequiresAdmin == (c.Level == CleaningLevel.Deep)));
    Check("nettoyage : racine de disque interdite", CleaningCatalog.IsForbiddenRoot(@"C:\") && CleaningCatalog.IsForbiddenRoot("C:"));
    Check("nettoyage : profil et Windows interdits",
        CleaningCatalog.IsForbiddenRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\")
        && CleaningCatalog.IsForbiddenRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
    Check("nettoyage : dossier Temp autorisé", !CleaningCatalog.IsForbiddenRoot(Path.GetTempPath()));
    Check("nettoyage : variable inconnue ignorée", CleaningScanner.ResolveFolders(@"%OPTIVANCE_INEXISTANTE%\Cache").Count == 0);
    Check("nettoyage : chemin relatif refusé", CleaningScanner.ResolveFolders(@"Cache").Count == 0);
    Check("nettoyage : Windows refusé comme dossier entier", CleaningScanner.ResolveFolders("%WINDIR%").Count == 0);
}

{
    // Nettoyage : aperçu et suppression dans un dossier temporaire.
    var root = Path.Combine(Path.GetTempPath(), "optivance-clean-" + Guid.NewGuid().ToString("N"));
    var outside = Path.Combine(Path.GetTempPath(), "optivance-outside-" + Guid.NewGuid().ToString("N"));
    try
    {
        var old = DateTime.UtcNow.AddDays(-3);
        void Make(string path, int size, bool isOld)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
            if (isOld)
            {
                File.SetCreationTimeUtc(path, old);
                File.SetLastWriteTimeUtc(path, old);
            }
        }
        Make(Path.Combine(root, "p1", "Cache", "a.bin"), 100, true);
        Make(Path.Combine(root, "p1", "Cache", "sub", "b.bin"), 50, true);
        Make(Path.Combine(root, "p1", "Cache", "recent.bin"), 70, false);
        Make(Path.Combine(root, "p2", "Cache", "c.bin"), 10, true);
        Make(Path.Combine(root, "p2", "Garder", "d.bin"), 10, true);
        Make(Path.Combine(outside, "precieux.txt"), 5, true);
        // Jonction dans le cache vers un dossier extérieur : ne doit être ni suivie ni vidée.
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(root, "p1", "Cache", "lien")}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true }))
        {
            mklink!.WaitForExit();
        }
        var category = new CleaningCategory("test", "Test", "", CleaningLevel.Standard, [new(Path.Combine(root, "*", "Cache"), MinimumAgeHours: 24)]);
        var analysis = CleaningScanner.Analyze(category, DateTime.UtcNow);
        Check("nettoyage : dossiers « * » résolus", analysis.Roots.Count == 2, string.Join(";", analysis.Roots));
        Check("nettoyage : fichiers récents et jonction exclus", analysis.Files.Count == 3 && analysis.Bytes == 160, $"{analysis.Files.Count} / {analysis.Bytes}");
        var outcome = CleaningScanner.Execute(analysis, DateTime.UtcNow, recycle: null);
        Check("nettoyage : suppression", outcome.Deleted == 3 && outcome.FreedBytes == 160 && outcome.Skipped == 0, outcome.ToString());
        Check("nettoyage : fichier récent conservé", File.Exists(Path.Combine(root, "p1", "Cache", "recent.bin")));
        Check("nettoyage : hors catalogue conservé", File.Exists(Path.Combine(root, "p2", "Garder", "d.bin")));
        Check("nettoyage : cible de la jonction intacte", File.Exists(Path.Combine(outside, "precieux.txt")));
        Check("nettoyage : dossier vidé retiré, emplacement conservé", !Directory.Exists(Path.Combine(root, "p1", "Cache", "sub")) && Directory.Exists(Path.Combine(root, "p2", "Cache")));

        // Fichier en cours d'utilisation : ignoré sans erreur.
        Make(Path.Combine(root, "p2", "Cache", "ouvert.bin"), 8, true);
        var second = CleaningScanner.Analyze(category, DateTime.UtcNow);
        using (File.Open(Path.Combine(root, "p2", "Cache", "ouvert.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var locked = CleaningScanner.Execute(second, DateTime.UtcNow, recycle: null);
            Check("nettoyage : fichier ouvert ignoré", locked.Deleted == 0 && locked.Skipped == 1, locked.ToString());
        }

        // Fichier disparu ou modifié entre l'aperçu et la suppression.
        File.SetLastWriteTimeUtc(Path.Combine(root, "p2", "Cache", "ouvert.bin"), DateTime.UtcNow);
        var changed = CleaningScanner.Execute(second, DateTime.UtcNow, recycle: null);
        Check("nettoyage : fichier modifié depuis l'aperçu conservé", changed.Deleted == 0 && File.Exists(Path.Combine(root, "p2", "Cache", "ouvert.bin")));

        // Corbeille : le lot est transmis tel quel.
        Make(Path.Combine(root, "p2", "Cache", "e.bin"), 4, true);
        List<string> recycled = [];
        var third = CleaningScanner.Analyze(category, DateTime.UtcNow);
        CleaningScanner.Execute(third, DateTime.UtcNow, batch => { recycled.AddRange(batch); foreach (var f in batch) File.Delete(f); });
        Check("nettoyage : envoi à la Corbeille par lot", recycled.Count == 1 && recycled[0].EndsWith("e.bin"));
    }
    finally
    {
        var junction = Path.Combine(root, "p1", "Cache", "lien");
        if (Directory.Exists(junction))
        {
            Directory.Delete(junction); // supprime la jonction, pas sa cible
        }
        foreach (var folder in new[] { root, outside })
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

{
    // Optimisation : formats Windows et sauvegarde des valeurs d'origine.
    Check("démarrage : absent = activé", OptimizationCatalog.IsStartupApproved(null));
    Check("démarrage : 02 activé, 03 désactivé", OptimizationCatalog.IsStartupApproved([2, 0, 0]) && !OptimizationCatalog.IsStartupApproved([3, 0, 0]) && !OptimizationCatalog.IsStartupApproved([7]));
    Check("démarrage : valeur écrite relue", !OptimizationCatalog.IsStartupApproved(OptimizationCatalog.StartupApprovedValue(false, DateTime.UtcNow))
        && OptimizationCatalog.IsStartupApproved(OptimizationCatalog.StartupApprovedValue(true, DateTime.UtcNow)));
    Check("service : types de démarrage", OptimizationCatalog.StartMode(2, 1) == "delayed-auto" && OptimizationCatalog.StartMode(2, null) == "auto"
        && OptimizationCatalog.StartMode(3, null) == "demand" && OptimizationCatalog.StartMode(4, null) == "disabled" && OptimizationCatalog.StartMode(null, null) == "unknown");
    Check("optimisation : noms de services uniques", OptimizationCatalog.Services.Select(s => s.Name).Distinct().Count() == OptimizationCatalog.Services.Count);
    var file = Path.Combine(Path.GetTempPath(), $"optivance-backup-{Guid.NewGuid():N}.json");
    try
    {
        var backup = new OptimizationBackup(file);
        backup.Remember("svc:SysMain", "auto");
        backup.Remember("svc:SysMain", "disabled");
        var reloaded = new OptimizationBackup(file);
        Check("sauvegarde : première valeur d'origine conservée", reloaded.TryGet("svc:SysMain", out var original) && original == "auto");
        reloaded.Forget("svc:SysMain");
        Check("sauvegarde : oubli persistant", new OptimizationBackup(file).Values.Count == 0);
        File.WriteAllText(file, "corrompu");
        Check("sauvegarde : fichier corrompu toléré", new OptimizationBackup(file).Values.Count == 0);
    }
    finally
    {
        File.Delete(file);
    }
}

{
    // Alertes : une seule notification par dépassement, réarmement sous le seuil moins la marge.
    var tracker = new AlertTracker();
    Check("alerte : sous le seuil", !tracker.Evaluate("t", 80, 90, 3, true));
    Check("alerte : franchissement", tracker.Evaluate("t", 91, 90, 3, true) && tracker.IsActive("t"));
    Check("alerte : pas de répétition", !tracker.Evaluate("t", 95, 90, 3, true));
    Check("alerte : reste active dans la marge", !tracker.Evaluate("t", 88, 90, 3, true) && tracker.IsActive("t"));
    Check("alerte : réarmée", !tracker.Evaluate("t", 86, 90, 3, true) && !tracker.IsActive("t"));
    Check("alerte : redéclenchée", tracker.Evaluate("t", 90, 90, 3, true));
    Check("alerte : mesure absente sans effet", !tracker.Evaluate("t", null, 90, 3, true) && tracker.IsActive("t"));
    Check("alerte : désactivée efface", !tracker.Evaluate("t", 99, 90, 3, true, enabled: false) && !tracker.IsActive("t"));
    Check("alerte disque : espace libre bas", tracker.Evaluate("d", 8, 10, 2, false) && !tracker.Evaluate("d", 11, 10, 2, false) && tracker.IsActive("d"));
}

{
    // Gros fichiers et doublons : vrais fichiers temporaires.
    var root = Path.Combine(Path.GetTempPath(), "optivance-dup-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var content = new byte[200_000];
        new Random(7).NextBytes(content);
        var different = (byte[])content.Clone();
        different[^1] ^= 0xFF; // même taille, même début, fin différente
        File.WriteAllBytes(Path.Combine(root, "a.mp4"), content);
        File.WriteAllBytes(Path.Combine(root, "b.mp4"), content);
        File.WriteAllBytes(Path.Combine(root, "c.mp4"), different);
        File.WriteAllBytes(Path.Combine(root, "d.iso"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(root, "e.iso"), new byte[5000]);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        var recent = DateTime.UtcNow.ToFileTimeUtc();
        List<ScanEntry> entries =
        [
            new(0, -1, 1, 6, true, "R", 615_000, 615_000, false, recent),
            new(1, 0, 0, 0, false, "a.mp4", 200_000, 200_000, false, old),
            new(2, 0, 0, 0, false, "b.mp4", 200_000, 200_000, false, recent),
            new(3, 0, 0, 0, false, "c.mp4", 200_000, 200_000, false, old),
            new(4, 0, 0, 0, false, "d.iso", 5000, 5000, false, old),
            new(5, 0, 0, 0, false, "e.iso", 5000, 5000, false, old),
            new(6, 0, 0, 0, false, "lien.mp4", 200_000, 200_000, true, old), // lien physique : ignoré
        ];
        var tree = new StorageTree(root, entries);
        Check("filtre : extensions", FileFilter.ParseExtensions("MP4, .iso *.mkv")!.SetEquals([".mp4", ".iso", ".mkv"]) && FileFilter.ParseExtensions("  ") is null);
        Check("gros fichiers : taille minimale", FileFinder.LargeFiles(tree, new FileFilter(MinimumBytes: 100_000)).Order().SequenceEqual([1, 2, 3]));
        Check("gros fichiers : extension", FileFinder.LargeFiles(tree, new FileFilter(Extensions: FileFilter.ParseExtensions("iso"))).Order().SequenceEqual([4, 5]));
        Check("gros fichiers : plus anciens que", FileFinder.LargeFiles(tree, new FileFilter(MinimumBytes: 100_000, ModifiedBefore: DateTime.UtcNow.AddYears(-1))).Order().SequenceEqual([1, 3]));
        Check("gros fichiers : lien physique exclu", !FileFinder.LargeFiles(tree, new FileFilter()).Contains(6));
        var groups = await FileFinder.DuplicatesAsync(tree, new FileFilter(), null, CancellationToken.None);
        var mp4 = groups.SingleOrDefault(g => g.Size == 200_000);
        Check("doublons : contenu identique seulement", mp4 is not null && mp4.Ids.Order().SequenceEqual([1, 2]), string.Join(" | ", groups.Select(g => string.Join(",", g.Ids))));
        Check("doublons : petits fichiers comparés", groups.Any(g => g.Size == 5000 && g.Ids.Count == 2));
        Check("doublons : espace récupérable", mp4?.Recoverable == 200_000);
        Check("doublons : garder le plus récent", FileFinder.AllButNewest(tree, [mp4!]).SequenceEqual([1]));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

{
    // Carte AMD : lecture des capteurs PMLog (indices d'adl_defines.h).
    var supported = new int[256];
    var values = new int[256];
    (supported[8], values[8]) = (1, 62);    // bord
    (supported[27], values[27]) = (1, 78);  // point chaud
    (supported[23], values[23]) = (1, 145); // consommation
    (supported[1], values[1]) = (1, 2450);  // fréquence
    (supported[19], values[19]) = (1, 97);  // activité
    var amd = MonitoringMath.AmdPmLog(supported, values);
    Check("AMD : capteurs lus", amd == new AmdGpuSensors(62, 78, 145, 2450, 97), amd.ToString());
    supported[8] = 0;
    Check("AMD : point chaud si bord absent", MonitoringMath.AmdPmLog(supported, values).TemperatureCelsius == 78);
    values[27] = 0;
    values[23] = 99999;
    var absurd = MonitoringMath.AmdPmLog(supported, values);
    Check("AMD : valeurs absurdes ignorées", absurd.TemperatureCelsius is null && absurd.PowerWatts is null);
    Check("AMD : rien de fourni", MonitoringMath.AmdPmLog([], []) == new AmdGpuSensors(null, null, null, null, null));
    // Puce intégrée (relevé réel d'un Ryzen 9600X) : pas de capteur « bord », température graphique (28).
    var integrated = new int[256];
    var readings = new int[256];
    foreach (var (index, value) in new[] { (1, 600), (23, 85), (28, 41), (29, 44), (32, 0) })
    {
        (integrated[index], readings[index]) = (index == 32 ? 0 : 1, value);
    }
    var apu = MonitoringMath.AmdPmLog(integrated, readings);
    Check("AMD intégrée : température graphique", apu.TemperatureCelsius == 41 && apu.ProcessorCelsius is null, apu.ToString());
    (integrated[32], readings[32]) = (1, 67);
    Check("AMD intégrée : température processeur si fournie", MonitoringMath.AmdPmLog(integrated, readings).ProcessorCelsius == 67);
}

{
    // Désinstallation : le script ne supprime que les fichiers d'OPTIVANCE, jamais les autres.
    var root = Path.Combine(Path.GetTempPath(), "optivance-uninstall-" + Guid.NewGuid().ToString("N"));
    var app = Path.Combine(root, "Mes trucs (l'app)");
    var data = Path.Combine(root, "data");
    try
    {
        Directory.CreateDirectory(Path.Combine(app, "moteur"));
        Directory.CreateDirectory(Path.Combine(data, "logs"));
        foreach (var file in new[] { "OPTIVANCE.exe", UninstallScript.ShortcutName, @"moteur\optivance-engine.exe", @"moteur\optivance-mft-helper.exe", "photo-perso.jpg" })
        {
            File.WriteAllText(Path.Combine(app, file), "x");
        }
        File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(data, @"logs\erreur.txt"), "x");
        using var waiter = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        var script = UninstallScript.Build(waiter.Id, Path.Combine(app, "OPTIVANCE.exe"), data);
        using (var run = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {UninstallScript.Encode(script)}") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            run.WaitForExit(30_000);
        }
        Check("désinstallation : fichiers d'OPTIVANCE supprimés", !File.Exists(Path.Combine(app, "OPTIVANCE.exe")) && !Directory.Exists(Path.Combine(app, "moteur")) && !File.Exists(Path.Combine(app, UninstallScript.ShortcutName)));
        Check("désinstallation : données supprimées", !Directory.Exists(data));
        Check("désinstallation : fichier personnel conservé", File.Exists(Path.Combine(app, "photo-perso.jpg")));
        Check("désinstallation : dossier non vide conservé", Directory.Exists(app));
        File.Delete(Path.Combine(app, "photo-perso.jpg"));
        File.WriteAllText(Path.Combine(app, "OPTIVANCE.exe"), "x");
        using (var again = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {UninstallScript.Encode(UninstallScript.Build(0, Path.Combine(app, "OPTIVANCE.exe"), data))}") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            again.WaitForExit(30_000);
        }
        Check("désinstallation : dossier vide retiré", !Directory.Exists(app));
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

{
    // Détection automatique des jeux (widget en jeu).
    var detector = new GameDetector();
    var t = new DateTime(2026, 11, 14, 12, 0, 0);
    var game = new ForegroundSample(100, "javaw", Fullscreen: true, Fps: 144);
    Check("jeu : un seul relevé ne suffit pas", !detector.Update(game, t) && detector.GameProcessId is null);
    Check("jeu : détecté au deuxième relevé", detector.Update(game, t.AddSeconds(1)) && detector.GameProcessId == 100 && detector.GameName == "javaw");
    Check("jeu : écran de chargement toléré", !detector.Update(game with { Fps = null }, t.AddSeconds(4)) && detector.GameProcessId == 100);
    Check("jeu : quitté après le délai sans images", detector.Update(game with { Fps = null }, t.AddSeconds(9)) && detector.GameProcessId is null);
    detector.Update(game, t.AddSeconds(10));
    detector.Update(game, t.AddSeconds(11));
    Check("jeu : alt-tab = sortie immédiate", detector.Update(new ForegroundSample(5, "Discord", false, null), t.AddSeconds(12)) && detector.GameProcessId is null);
    var browser = new ForegroundSample(7, "chrome", true, 60);
    detector.Update(browser, t.AddSeconds(13));
    Check("jeu : navigateur plein écran exclu", !detector.Update(browser, t.AddSeconds(14)) && detector.GameProcessId is null);
    var windowed = game with { Fullscreen = false };
    detector.Update(windowed, t.AddSeconds(15));
    Check("jeu : fenêtré non détecté", !detector.Update(windowed, t.AddSeconds(16)) && detector.GameProcessId is null);
    var slow = game with { Fps = 12 };
    detector.Update(slow, t.AddSeconds(17));
    Check("jeu : moins de 20 images/s non détecté", !detector.Update(slow, t.AddSeconds(18)));
}

{
    // Mises à jour : versions, fréquence, empreinte.
    Check("maj : étiquettes lues", UpdateRules.ParseTag("v1.2.0") == new Version(1, 2, 0) && UpdateRules.ParseTag("1.2") == new Version(1, 2, 0) && UpdateRules.ParseTag("v2.0.1-beta") == new Version(2, 0, 1));
    Check("maj : étiquette illisible", UpdateRules.ParseTag("latest") is null && UpdateRules.ParseTag("") is null);
    Check("maj : plus récente", UpdateRules.IsNewer(new Version(1, 1, 1), new Version(1, 2, 0)) && UpdateRules.IsNewer(new Version(1, 1, 1, 0), new Version(1, 1, 2)));
    Check("maj : même version ou plus ancienne", !UpdateRules.IsNewer(new Version(1, 1, 1, 0), new Version(1, 1, 1)) && !UpdateRules.IsNewer(new Version(1, 2, 0), new Version(1, 1, 9)));
    var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    Check("maj : vérification une fois par jour", UpdateRules.CheckDue(null, now) && !UpdateRules.CheckDue(now.AddHours(-3), now) && UpdateRules.CheckDue(now.AddHours(-25), now) && UpdateRules.CheckDue(now.AddDays(2), now));
    var sha = new string('a', 64);
    Check("maj : empreinte lue", UpdateRules.ParseSha256($"{sha.ToUpperInvariant()}  OPTIVANCE.exe\n") == sha && UpdateRules.ParseSha256("pas une empreinte") is null);
}

Console.WriteLine($"{passed} réussis, {failures} échoués");
return failures == 0 ? 0 : 1;
