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

Console.WriteLine($"{passed} réussis, {failures} échoués");
return failures == 0 ? 0 : 1;
