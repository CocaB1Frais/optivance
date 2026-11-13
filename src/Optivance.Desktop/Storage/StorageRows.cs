using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using Optivance.Core.Storage;

namespace Optivance.Desktop.Storage;

/// <summary>Glyphes Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
internal static class Glyphs
{
    public const string Folder = "";
    public const string File = "";
    public const string Drive = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string Link = "";
}

internal static class Palette
{
    private static readonly Dictionary<int, SolidColorBrush> Cache = [];

    public static SolidColorBrush Brush(int rgb)
    {
        if (!Cache.TryGetValue(rgb, out var brush))
        {
            brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            Cache[rgb] = brush;
        }
        return brush;
    }

    public static readonly SolidColorBrush FolderBrush = Brush(0xE8C15A);
}

/// <summary>Ligne visible de l'arborescence (projection à plat des dossiers dépliés).</summary>
internal sealed class TreeRow : INotifyPropertyChanged
{
    public const double BarMaxWidth = 64;
    private bool _isExpanded;

    public TreeRow(StorageTree tree, int id, int depth, Func<int, int> colorOf)
    {
        Id = id;
        Depth = depth;
        var parent = tree.Parent(id);
        var parentWeight = parent >= 0 ? tree.Weight(parent) : tree.Weight(id);
        Fraction = parentWeight == 0 ? 0 : (double)tree.Weight(id) / parentWeight;
        IsDirectory = tree.IsDirectory(id);
        IsDuplicate = tree.IsDuplicate(id);
        HasChildren = tree.HasChildren(id);
        Name = id == 0 ? tree.RootPath : tree.Name(id);
        Glyph = id == 0 ? Glyphs.Drive : IsDirectory ? Glyphs.Folder : IsDuplicate ? Glyphs.Link : Glyphs.File;
        GlyphBrush = IsDirectory ? Palette.FolderBrush : Palette.Brush(colorOf(id));
        SizeText = StorageFormat.Bytes(tree.Size(id));
        AllocatedText = StorageFormat.Bytes(tree.Allocated(id));
        ItemsText = IsDirectory ? StorageFormat.Count(tree.Items(id)) : "";
        FilesText = IsDirectory ? StorageFormat.Count(tree.Files(id)) : "";
        FoldersText = IsDirectory ? StorageFormat.Count(tree.Folders(id)) : "";
        ModifiedText = StorageFormat.Date(tree.Modified(id));
        PercentText = StorageFormat.Percent(Fraction);
        ToolTip = IsDuplicate ? "Lien physique supplémentaire : la taille est déjà comptée ailleurs." : null;
    }

    public int Id { get; }
    public int Depth { get; }
    public double Indent => Depth * 18;
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool IsDuplicate { get; }
    public bool HasChildren { get; }
    public string Glyph { get; }
    public Brush GlyphBrush { get; }
    public double Fraction { get; }
    public double BarWidth => Math.Max(Fraction > 0 ? 1 : 0, Fraction * BarMaxWidth);
    public string PercentText { get; }
    public string SizeText { get; }
    public string AllocatedText { get; }
    public string ItemsText { get; }
    public string FilesText { get; }
    public string FoldersText { get; }
    public string ModifiedText { get; }
    public string? ToolTip { get; }
    public double NameOpacity => IsDuplicate ? 0.6 : 1;
    public string Expander => HasChildren ? (IsExpanded ? Glyphs.ChevronDown : Glyphs.ChevronRight) : "";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Expander)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Nom accessible (lecteurs d'écran, automatisation).</summary>
    public override string ToString() => Name;
}

/// <summary>
/// Collection à plat pour un ListView virtualisé : les modifications groupées (dépliage de
/// milliers d'enfants) n'émettent qu'une notification.
/// </summary>
internal sealed class RowCollection<T> : IList, INotifyCollectionChanged where T : class
{
    private List<T> _items = [];

    public int Count => _items.Count;
    public T this[int index] => _items[index];
    public List<T> Items => _items;

    public void Reset(List<T> items)
    {
        _items = items;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Changed() => CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    public int IndexOf(T item) => _items.IndexOf(item);

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    object? IList.this[int index] { get => _items[index]; set => throw new NotSupportedException(); }
    bool IList.IsFixedSize => false;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is T item && _items.Contains(item);
    int IList.IndexOf(object? value) => value is T item ? _items.IndexOf(item) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

/// <summary>Ligne de la liste des fichiers (créée à la demande).</summary>
internal sealed class FileRow
{
    public FileRow(StorageTree tree, int id, Func<int, int> colorOf)
    {
        Id = id;
        Name = tree.Name(id);
        GlyphBrush = Palette.Brush(colorOf(id));
        Glyph = tree.IsDuplicate(id) ? Glyphs.Link : Glyphs.File;
        SizeText = StorageFormat.Bytes(tree.Size(id));
        AllocatedText = StorageFormat.Bytes(tree.Allocated(id));
        ModifiedText = StorageFormat.Date(tree.Modified(id));
        var parent = tree.Parent(id);
        Folder = parent >= 0 ? tree.GetPath(parent) : "";
        Extension = StorageTree.ExtensionOf(Name);
    }

    public int Id { get; }
    public string Name { get; }
    public string Glyph { get; }
    public Brush GlyphBrush { get; }
    public string SizeText { get; }
    public string AllocatedText { get; }
    public string ModifiedText { get; }
    public string Folder { get; }
    public string Extension { get; }

    public override bool Equals(object? obj) => obj is FileRow other && other.Id == Id;
    public override int GetHashCode() => Id;
    public override string ToString() => Name;
}

/// <summary>Liste virtuelle : seules les lignes affichées sont construites.</summary>
internal sealed class VirtualFileList(StorageTree tree, int[] ids, Func<int, int> colorOf) : IList
{
    private readonly Dictionary<int, FileRow> _cache = [];

    public int[] Ids => ids;
    public int Count => ids.Length;

    public FileRow Get(int index)
    {
        var id = ids[index];
        if (!_cache.TryGetValue(id, out var row))
        {
            if (_cache.Count > 4096)
            {
                _cache.Clear();
            }
            row = new FileRow(tree, id, colorOf);
            _cache[id] = row;
        }
        return row;
    }

    public int IndexOfId(int id) => Array.IndexOf(ids, id);

    object? IList.this[int index] { get => Get(index); set => throw new NotSupportedException(); }
    bool IList.IsFixedSize => true;
    bool IList.IsReadOnly => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    bool IList.Contains(object? value) => value is FileRow row && Array.IndexOf(ids, row.Id) >= 0;
    int IList.IndexOf(object? value) => value is FileRow row ? Array.IndexOf(ids, row.Id) : -1;
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
    void ICollection.CopyTo(Array array, int index)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            array.SetValue(Get(i), index + i);
        }
    }
    IEnumerator IEnumerable.GetEnumerator()
    {
        for (var i = 0; i < ids.Length; i++)
        {
            yield return Get(i);
        }
    }
}

/// <summary>Ligne du panneau des extensions.</summary>
internal sealed class ExtensionRow(ExtensionStats stats, ulong total, int rgb)
{
    public ExtensionStats Stats { get; } = stats;
    public string Extension => Stats.Extension;
    public string Description => ExtensionCatalog.Describe(Stats.Extension);
    public Brush Swatch { get; } = Palette.Brush(rgb);
    public string SizeText => StorageFormat.Bytes(Stats.Bytes);
    public string FilesText => StorageFormat.Count(Stats.Files);
    public double Fraction => total == 0 ? 0 : (double)Stats.Bytes / total;
    public string PercentText => StorageFormat.Percent(Fraction);
    public double BarWidth => Math.Max(Fraction > 0 ? 1 : 0, Fraction * 70);
}

/// <summary>Disque proposé dans le sélecteur.</summary>
internal sealed class DriveItem
{
    public required string Root { get; init; }
    public required string Label { get; init; }
    public required string Detail { get; init; }
    public double UsedFraction { get; init; }
    public double BarWidth => UsedFraction * 120;
    public bool IsNtfs { get; init; }

    public override string ToString() => Label;

    public static List<DriveItem> Load()
    {
        var drives = new List<DriveItem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }
                var used = (ulong)(drive.TotalSize - drive.TotalFreeSpace);
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? (drive.DriveType == DriveType.Removable ? "Disque amovible" : "Disque local") : drive.VolumeLabel;
                drives.Add(new DriveItem
                {
                    Root = drive.RootDirectory.FullName,
                    Label = $"{label} ({drive.Name.TrimEnd('\\')})",
                    Detail = $"{StorageFormat.Bytes(used)} utilisés sur {StorageFormat.Bytes((ulong)drive.TotalSize)} · {drive.DriveFormat}",
                    UsedFraction = drive.TotalSize == 0 ? 0 : (double)used / drive.TotalSize,
                    IsNtfs = drive.DriveFormat == "NTFS"
                });
            }
            catch (IOException)
            {
                // Lecteur retiré pendant l'énumération.
            }
            catch (UnauthorizedAccessException)
            {
                // Lecteur non lisible par ce compte.
            }
        }
        return drives;
    }
}
