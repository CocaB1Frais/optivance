using System.Security.Cryptography;

namespace Optivance.Core.Storage;

/// <summary>Critères de recherche des gros fichiers et des doublons.</summary>
public sealed record FileFilter(ulong MinimumBytes = 0, DateTime? ModifiedBefore = null, DateTime? ModifiedAfter = null, IReadOnlySet<string>? Extensions = null)
{
    /// <summary>« mp4, .ISO  mkv » → {".mp4", ".iso", ".mkv"} ; vide = toutes les extensions.</summary>
    public static IReadOnlySet<string>? ParseExtensions(string? text)
    {
        var parts = (text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => "." + part.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(part => part.Length > 1)
            .ToHashSet();
        return parts.Count == 0 ? null : parts;
    }
}

/// <summary>Groupe de fichiers au contenu identique.</summary>
public sealed record DuplicateGroup(ulong Size, IReadOnlyList<int> Ids)
{
    /// <summary>Espace récupérable en gardant une seule copie.</summary>
    public ulong Recoverable => Size * (ulong)Math.Max(0, Ids.Count - 1);
}

/// <summary>
/// Gros fichiers et doublons à partir de l'index Storage (aucune nouvelle analyse du disque).
/// Doublons : regroupement par taille, puis empreinte partielle (début et fin), puis empreinte
/// SHA-256 complète : deux fichiers ne sont déclarés identiques que si leur contenu l'est.
/// Les liens physiques supplémentaires (même fichier) ne sont jamais proposés.
/// </summary>
public static class FileFinder
{
    private const int PartialBytes = 64 * 1024;

    public static bool Matches(StorageTree tree, int id, FileFilter filter)
    {
        if (id <= 0 || tree.IsDirectory(id) || tree.IsDeleted(id) || tree.IsDuplicate(id) || tree.Size(id) < filter.MinimumBytes)
        {
            return false;
        }
        var modified = tree.Modified(id);
        if (filter.ModifiedBefore is { } before && (modified <= 0 || DateTime.FromFileTimeUtc(modified) > before))
        {
            return false;
        }
        if (filter.ModifiedAfter is { } after && (modified <= 0 || DateTime.FromFileTimeUtc(modified) < after))
        {
            return false;
        }
        if (filter.Extensions is { } extensions)
        {
            var extension = tree.ExtensionId(id);
            return extension >= 0 && extensions.Contains(tree.Extensions[extension].Extension);
        }
        return true;
    }

    /// <summary>Fichiers correspondant aux critères, du plus gros au plus petit.</summary>
    public static List<int> LargeFiles(StorageTree tree, FileFilter filter, int limit = 2000) =>
        tree.FilesBySize().Where(id => Matches(tree, id, filter)).Take(limit).ToList();

    /// <summary>Candidats doublons : fichiers non vides de même taille (au moins deux).</summary>
    public static List<List<int>> SameSizeGroups(StorageTree tree, FileFilter filter) =>
        tree.FilesBySize()
            .Where(id => tree.Size(id) > 0 && Matches(tree, id, filter))
            .GroupBy(tree.Size)
            .Where(group => group.Count() > 1)
            .Select(group => group.ToList())
            .ToList();

    public static async Task<List<DuplicateGroup>> DuplicatesAsync(StorageTree tree, FileFilter filter, IProgress<(long Done, long Total)>? progress, CancellationToken cancellation)
    {
        var candidates = SameSizeGroups(tree, filter);
        long total = candidates.Sum(group => (long)tree.Size(group[0]) * group.Count);
        long done = 0;
        List<DuplicateGroup> result = [];
        foreach (var group in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            var size = tree.Size(group[0]);
            // Étape 1 : début + fin (élimine vite la plupart des faux candidats).
            var partial = new Dictionary<string, List<int>>();
            foreach (var id in group)
            {
                var key = await Task.Run(() => PartialHash(tree.GetPath(id), size), cancellation);
                if (key is not null)
                {
                    (partial.TryGetValue(key, out var list) ? list : partial[key] = []).Add(id);
                }
            }
            // Étape 2 : contenu complet pour les candidats restants.
            foreach (var same in partial.Values.Where(list => list.Count > 1))
            {
                var full = new Dictionary<string, List<int>>();
                foreach (var id in same)
                {
                    var key = size <= PartialBytes * 2 ? "petit" : await Task.Run(() => FullHash(tree.GetPath(id), cancellation), cancellation);
                    done += (long)size;
                    progress?.Report((done, total));
                    if (key is not null)
                    {
                        (full.TryGetValue(key, out var list) ? list : full[key] = []).Add(id);
                    }
                }
                result.AddRange(full.Values.Where(list => list.Count > 1).Select(list => new DuplicateGroup(size, list)));
            }
            done += (long)size * (group.Count - partial.Values.Where(list => list.Count > 1).Sum(list => list.Count));
            progress?.Report((done, total));
        }
        return result.OrderByDescending(group => group.Recoverable).ToList();
    }

    /// <summary>Empreinte de taille + premiers et derniers 64 Ko ; null si illisible.</summary>
    public static string? PartialHash(string path, ulong size)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            var buffer = new byte[PartialBytes * 2];
            var read = stream.ReadAtLeast(buffer.AsSpan(0, PartialBytes), PartialBytes, throwOnEndOfStream: false);
            if (stream.Length > PartialBytes * 2)
            {
                stream.Seek(-PartialBytes, SeekOrigin.End);
                read += stream.ReadAtLeast(buffer.AsSpan(read, PartialBytes), PartialBytes, throwOnEndOfStream: false);
            }
            else if (stream.Length > PartialBytes)
            {
                read += stream.ReadAtLeast(buffer.AsSpan(read), (int)(stream.Length - read), throwOnEndOfStream: false);
            }
            return $"{size}:{Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)))}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? FullHash(string path, CancellationToken cancellation)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Sélection automatique : toutes les copies sauf la plus récente de chaque groupe.</summary>
    public static IEnumerable<int> AllButNewest(StorageTree tree, IEnumerable<DuplicateGroup> groups) =>
        groups.SelectMany(group => group.Ids.OrderByDescending(tree.Modified).Skip(1));
}
