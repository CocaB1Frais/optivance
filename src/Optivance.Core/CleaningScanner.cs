using System.IO.Enumeration;

namespace Optivance.Core;

/// <summary>Fichier candidat au nettoyage, avec l'emplacement du catalogue qui l'a trouvé.</summary>
public sealed record CleaningFile(string Path, long Bytes, string Root);

/// <summary>Résultat de l'aperçu d'une catégorie.</summary>
public sealed record CleaningAnalysis(CleaningCategory Category, IReadOnlyList<CleaningFile> Files, IReadOnlyList<string> Roots)
{
    public long Bytes { get; } = Files.Sum(file => file.Bytes);
}

/// <summary>Réponse du composant administrateur pour une catégorie approfondie.</summary>
public sealed record DeepCleanReport(string Category, string Mode, string State, long Files, long Bytes, long Deleted, long Freed, long Skipped, long Refused);

/// <summary>Résultat d'un nettoyage.</summary>
public sealed record CleaningOutcome(string CategoryId, long FreedBytes, int Deleted, int Skipped);

/// <summary>
/// Aperçu et suppression pour les catégories standard (droits de l'utilisateur).
/// Règles : emplacements du catalogue uniquement, aucun lien symbolique ni jonction suivi ou
/// supprimé, âge minimal respecté, fichiers en cours d'utilisation ignorés, revérification de
/// chaque fichier juste avant la suppression.
/// </summary>
public static class CleaningScanner
{
    private static readonly EnumerationOptions Listing = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    /// <summary>Développe les variables d'environnement et les segments « * » (sans traverser de lien).</summary>
    public static IReadOnlyList<string> ResolveFolders(string folderPattern, bool allowProtectedRoot = false)
    {
        var expanded = Environment.ExpandEnvironmentVariables(folderPattern);
        if (expanded.Contains('%') || !Path.IsPathFullyQualified(expanded))
        {
            return [];
        }
        var segments = expanded.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        List<string> current = [segments[0] + "\\"];
        foreach (var segment in segments.Skip(1))
        {
            List<string> next = [];
            var wildcard = segment.IndexOfAny(['*', '?']) >= 0;
            foreach (var folder in current)
            {
                if (wildcard)
                {
                    try
                    {
                        next.AddRange(new DirectoryInfo(folder).EnumerateDirectories(segment, Listing)
                            .Where(child => !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            .Select(child => child.FullName));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                else
                {
                    var child = Path.Combine(folder, segment);
                    if (Directory.Exists(child) || File.Exists(child) || segment == segments[^1])
                    {
                        next.Add(child);
                    }
                }
            }
            current = next;
        }
        return current.Where(folder => Directory.Exists(folder) && !IsReparsePoint(folder) && (allowProtectedRoot || !CleaningCatalog.IsForbiddenRoot(folder)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static CleaningAnalysis Analyze(CleaningCategory category, DateTime nowUtc, CancellationToken cancellation = default)
    {
        List<CleaningFile> files = [];
        List<string> roots = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var target in category.Targets)
        {
            var cutoff = nowUtc.AddHours(-target.MinimumAgeHours);
            // Un dossier protégé (ex. %WINDIR%) n'est admis que pour un fichier précis, sans récursion.
            var precise = !target.Recursive && !target.WholeFolderContent && target.Pattern.IndexOfAny(['*', '?']) < 0;
            foreach (var root in ResolveFolders(target.Folder, precise))
            {
                roots.Add(root);
                Collect(root, root, target, cutoff, files, seen, cancellation);
            }
        }
        return new CleaningAnalysis(category, files, roots);
    }

    private static void Collect(string root, string folder, CleaningTarget target, DateTime cutoff, List<CleaningFile> files, HashSet<string> seen, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Listing).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var entry in entries)
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }
            if (entry is DirectoryInfo directory)
            {
                if (target.Recursive)
                {
                    Collect(root, directory.FullName, target, cutoff, files, seen, cancellation);
                }
            }
            else if (entry is FileInfo file
                && FileSystemName.MatchesSimpleExpression(target.Pattern, file.Name)
                && IsOldEnough(file, cutoff)
                && seen.Add(file.FullName))
            {
                files.Add(new CleaningFile(file.FullName, file.Length, root));
            }
        }
    }

    private static bool IsOldEnough(FileInfo file, DateTime cutoff) =>
        file.LastWriteTimeUtc <= cutoff && file.CreationTimeUtc <= cutoff;

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Supprime les fichiers de l'aperçu encore valides. <paramref name="recycle"/> envoie un lot à
    /// la Corbeille ; s'il est absent, la suppression est définitive. Les dossiers devenus vides
    /// sous les emplacements « contenu entier » sont retirés (jamais l'emplacement lui-même).
    /// </summary>
    public static CleaningOutcome Execute(CleaningAnalysis analysis, DateTime nowUtc, Action<IReadOnlyList<string>>? recycle, CancellationToken cancellation = default)
    {
        var roots = analysis.Roots.Select(root => root.TrimEnd('\\') + "\\").ToList();
        var minimumAge = analysis.Category.Targets.Select(target => target.MinimumAgeHours).DefaultIfEmpty(0).Min();
        var cutoff = nowUtc.AddHours(-minimumAge);
        List<CleaningFile> eligible = [];
        var skipped = 0;
        foreach (var file in analysis.Files)
        {
            var info = new FileInfo(file.Path);
            var valid = info.Exists
                && roots.Any(root => file.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                && !info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                && !HasReparseAncestor(info.DirectoryName, file.Root)
                && IsOldEnough(info, cutoff);
            if (valid)
            {
                eligible.Add(file with { Bytes = info.Length });
            }
            else
            {
                skipped++;
            }
        }

        if (recycle is not null)
        {
            foreach (var batch in eligible.Chunk(256))
            {
                cancellation.ThrowIfCancellationRequested();
                recycle(batch.Select(file => file.Path).ToList());
            }
        }
        else
        {
            foreach (var file in eligible)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(file.Path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // En cours d'utilisation ou protégé : ignoré, compté plus bas.
                }
            }
        }

        long freed = 0;
        var deleted = 0;
        foreach (var file in eligible)
        {
            if (File.Exists(file.Path))
            {
                skipped++;
            }
            else
            {
                deleted++;
                freed += file.Bytes;
            }
        }

        var wholeContent = analysis.Category.Targets.Any(target => target.WholeFolderContent && target.Recursive);
        if (wholeContent)
        {
            foreach (var root in analysis.Roots)
            {
                RemoveEmptyFolders(root, isRoot: true);
            }
        }
        return new CleaningOutcome(analysis.Category.Id, freed, deleted, skipped);
    }

    private static bool HasReparseAncestor(string? folder, string root)
    {
        var stop = root.TrimEnd('\\');
        while (!string.IsNullOrEmpty(folder) && folder.Length >= stop.Length)
        {
            if (IsReparsePoint(folder))
            {
                return true;
            }
            if (string.Equals(folder.TrimEnd('\\'), stop, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            folder = Path.GetDirectoryName(folder);
        }
        return false;
    }

    private static void RemoveEmptyFolders(string folder, bool isRoot)
    {
        if (IsReparsePoint(folder))
        {
            return;
        }
        try
        {
            foreach (var child in Directory.EnumerateDirectories(folder, "*", Listing).ToList())
            {
                RemoveEmptyFolders(child, isRoot: false);
            }
            if (!isRoot && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder, recursive: false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
