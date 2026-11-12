using System.IO;
using Optivance.Core;

namespace Optivance.Desktop;

internal sealed class StorageIndex
{
    private readonly IReadOnlyList<ScanEntry> _entries;
    private readonly bool _complete;

    public string RootPath { get; }
    public int Count => _entries.Count;

    public StorageIndex(string rootPath, IReadOnlyList<ScanEntry> entries, bool complete)
    {
        RootPath = Path.GetFullPath(rootPath);
        _entries = entries;
        _complete = complete;
    }

    public ScanEntry GetEntry(int id) => _entries[id];

    public int GetChildStart(int directoryId) => _entries[directoryId].ChildStart;

    public int GetChildCount(int directoryId)
    {
        var entry = _entries[directoryId];
        if (!_complete)
        {
            return Math.Max(0, Math.Min(entry.ChildCount, _entries.Count - entry.ChildStart));
        }
        return entry.ChildCount;
    }

    public List<StorageRow> BuildRows(int directoryId, CancellationToken cancellationToken = default)
    {
        var start = GetChildStart(directoryId);
        var count = GetChildCount(directoryId);
        var rows = new List<StorageRow>(count);
        var end = start + count;
        for (var id = start; id < end; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = _entries[id];
            rows.Add(new StorageRow(
                entry.Id,
                entry.ParentId,
                entry.IsDirectory,
                entry.Name,
                GetPath(id),
                entry.LogicalBytes,
                entry.AllocatedBytes,
                entry.IsHardLinkDuplicate));
        }
        return rows;
    }

    public string GetPath(int id)
    {
        if (id == 0)
        {
            return RootPath;
        }

        var segments = new List<string>();
        var current = id;
        while (current > 0)
        {
            var entry = _entries[current];
            segments.Add(entry.Name);
            current = entry.ParentId;
        }

        var path = RootPath;
        for (var index = segments.Count - 1; index >= 0; index--)
        {
            path = Path.Combine(path, segments[index]);
        }
        return path;
    }

    public List<StorageBreadcrumb> BuildBreadcrumb(int directoryId)
    {
        var trail = new List<int>();
        var current = directoryId;
        while (current >= 0)
        {
            trail.Add(current);
            current = _entries[current].ParentId;
        }
        trail.Reverse();

        var result = new List<StorageBreadcrumb>(trail.Count);
        foreach (var id in trail)
        {
            result.Add(new StorageBreadcrumb(id, id == 0 ? RootPath : _entries[id].Name));
        }
        return result;
    }
}

internal sealed record StorageBreadcrumb(int Id, string Label);

public sealed record StorageRow(
    int Id,
    int ParentId,
    bool IsDirectory,
    string Name,
    string Path,
    ulong LogicalBytes,
    ulong AllocatedBytes,
    bool IsHardLinkDuplicate)
{
    public string Kind => IsDirectory ? "Dossier" : "Fichier";
    public string LogicalText => FormatBytes(LogicalBytes);
    public string AllocatedText => FormatBytes(AllocatedBytes);
    public string HardLinkText => IsHardLinkDuplicate ? "Alias · déjà compté" : "—";
    public ulong TreemapWeight => IsHardLinkDuplicate ? 0 : LogicalBytes;
    public string Category => IsDirectory ? "Dossiers" : StorageCategories.ForPath(Path);

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To", "Po"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:N1} {units[unit]}";
    }
}

internal static class StorageCategories
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".heic", ".jpeg", ".jpg", ".png", ".raw", ".svg", ".tif", ".tiff", ".webp"
    };
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avi", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".webm", ".wmv"
    };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".aiff", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma"
    };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".md", ".odt", ".pdf", ".ppt", ".pptx", ".rtf", ".txt", ".xls", ".xlsx"
    };
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".cab", ".gz", ".iso", ".rar", ".tar", ".zip"
    };
    private static readonly HashSet<string> Applications = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appx", ".com", ".dll", ".exe", ".msi", ".msix", ".sys"
    };
    private static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cpp", ".cs", ".css", ".go", ".html", ".java", ".js", ".json", ".py", ".rs", ".ts", ".xaml", ".xml", ".yaml", ".yml"
    };

    public static string ForPath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        if (Images.Contains(extension)) return "Images";
        if (Video.Contains(extension)) return "Vidéos";
        if (Audio.Contains(extension)) return "Audio";
        if (Documents.Contains(extension)) return "Documents";
        if (Archives.Contains(extension)) return "Archives";
        if (Applications.Contains(extension)) return "Applications";
        if (Code.Contains(extension)) return "Code";
        return "Autres";
    }
}
