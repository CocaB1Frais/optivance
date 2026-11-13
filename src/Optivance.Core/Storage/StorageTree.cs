namespace Optivance.Core.Storage;

public enum StorageSortKey
{
    Size,
    Allocated,
    Name,
    Items,
    Modified
}

/// <summary>Statistiques d'une extension (fichiers non supprimés).</summary>
public sealed class ExtensionStats(int id, string extension)
{
    public int Id { get; } = id;
    public string Extension { get; } = extension;
    /// <summary>Taille logique unique (un seul lien par fichier compté).</summary>
    public ulong Bytes { get; internal set; }
    public ulong AllocatedBytes { get; internal set; }
    public long Files { get; internal set; }
    /// <summary>Rang par taille décroissante (0 = la plus volumineuse), fixé à la construction.</summary>
    public int Rank { get; internal set; }
}

/// <summary>
/// Arbre d'analyse en tableaux (adapté à des centaines de milliers d'entrées).
/// Tailles des dossiers = totaux uniques du sous-arbre (hardlinks comptés une fois) ;
/// un lien secondaire (doublon) est affiché avec sa taille mais ne pèse rien.
/// La suppression marque le sous-arbre et met à jour les ancêtres, sans nouvelle analyse.
/// </summary>
public sealed class StorageTree
{
    public const string NoExtension = "(sans extension)";

    private readonly int[] _parent;
    private readonly int[] _childStart;
    private readonly int[] _childCount;
    private readonly bool[] _isDirectory;
    private readonly string[] _name;
    private readonly ulong[] _size;
    private readonly ulong[] _allocated;
    private readonly ulong[] _weight;
    private readonly ulong[] _weightAllocated;
    private readonly bool[] _duplicate;
    private readonly long[] _modified;
    private readonly long[] _files;
    private readonly long[] _folders;
    private readonly int[] _extension;
    private readonly bool[] _deleted;
    /// <summary>Enfants de chaque dossier triés par poids décroissant (mêmes plages que childStart).</summary>
    private readonly int[] _bySize;
    private readonly List<ExtensionStats> _extensions = [];

    public StorageTree(string rootPath, IReadOnlyList<ScanEntry> entries)
    {
        if (entries.Count == 0 || !entries[0].IsDirectory)
        {
            throw new ArgumentException("Index d'analyse sans racine.", nameof(entries));
        }
        RootPath = rootPath;
        var count = entries.Count;
        _parent = new int[count];
        _childStart = new int[count];
        _childCount = new int[count];
        _isDirectory = new bool[count];
        _name = new string[count];
        _size = new ulong[count];
        _allocated = new ulong[count];
        _weight = new ulong[count];
        _weightAllocated = new ulong[count];
        _duplicate = new bool[count];
        _modified = new long[count];
        _files = new long[count];
        _folders = new long[count];
        _extension = new int[count];
        _deleted = new bool[count];
        _bySize = new int[count];

        var extensionIds = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var id = 0; id < count; id++)
        {
            var entry = entries[id];
            _parent[id] = entry.ParentId;
            _childStart[id] = entry.ChildStart;
            _childCount[id] = Math.Clamp(entry.ChildCount, 0, Math.Max(0, count - entry.ChildStart));
            _isDirectory[id] = entry.IsDirectory;
            _name[id] = entry.Name;
            _size[id] = entry.LogicalBytes;
            _allocated[id] = entry.AllocatedBytes;
            _duplicate[id] = entry.IsHardLinkDuplicate;
            _modified[id] = entry.Modified;
            _bySize[id] = id;
            if (entry.IsDirectory)
            {
                _weight[id] = entry.LogicalBytes;
                _weightAllocated[id] = entry.AllocatedBytes;
                _extension[id] = -1;
            }
            else
            {
                _weight[id] = entry.IsHardLinkDuplicate ? 0 : entry.LogicalBytes;
                _weightAllocated[id] = entry.IsHardLinkDuplicate ? 0 : entry.AllocatedBytes;
                var extension = ExtensionOf(entry.Name);
                if (!extensionIds.TryGetValue(extension, out var extensionId))
                {
                    extensionId = _extensions.Count;
                    extensionIds.Add(extension, extensionId);
                    _extensions.Add(new ExtensionStats(extensionId, extension));
                }
                _extension[id] = extensionId;
                var stats = _extensions[extensionId];
                stats.Files++;
                stats.Bytes += _weight[id];
                stats.AllocatedBytes += _weightAllocated[id];
            }
        }

        // Compteurs de sous-arbre : les enfants ont toujours un identifiant supérieur au parent.
        for (var id = count - 1; id > 0; id--)
        {
            var parent = _parent[id];
            if (parent < 0)
            {
                continue;
            }
            if (_isDirectory[id])
            {
                _folders[parent] += _folders[id] + 1;
                _files[parent] += _files[id];
            }
            else
            {
                _files[parent]++;
            }
        }

        var keys = new ulong[count];
        for (var id = 0; id < count; id++)
        {
            keys[id] = ulong.MaxValue - _weight[id];
        }
        for (var id = 0; id < count; id++)
        {
            if (_isDirectory[id] && _childCount[id] > 1)
            {
                Array.Sort(keys, _bySize, _childStart[id], _childCount[id]);
            }
        }

        var rank = 0;
        foreach (var stats in _extensions.OrderByDescending(stats => stats.Bytes).ThenBy(stats => stats.Extension, StringComparer.Ordinal))
        {
            stats.Rank = rank++;
        }
    }

    public string RootPath { get; }
    public int Count => _parent.Length;
    public IReadOnlyList<ExtensionStats> Extensions => _extensions;

    public int Parent(int id) => _parent[id];
    public bool IsDirectory(int id) => _isDirectory[id];
    public string Name(int id) => id == 0 ? RootPath : _name[id];
    /// <summary>Taille affichée : sous-arbre unique pour un dossier, taille logique pour un fichier.</summary>
    public ulong Size(int id) => _isDirectory[id] ? _weight[id] : _size[id];
    public ulong Allocated(int id) => _isDirectory[id] ? _weightAllocated[id] : _allocated[id];
    /// <summary>Contribution unique aux totaux (0 pour un lien secondaire).</summary>
    public ulong Weight(int id) => _weight[id];
    public bool IsDuplicate(int id) => _duplicate[id];
    public bool IsDeleted(int id) => _deleted[id];
    public long Modified(int id) => _modified[id];
    public long Files(int id) => _files[id];
    public long Folders(int id) => _folders[id];
    public long Items(int id) => _files[id] + _folders[id];
    public int ExtensionId(int id) => _extension[id];
    public ulong TotalBytes => _weight[0];
    public ulong TotalAllocated => _weightAllocated[0];

    public bool HasChildren(int id)
    {
        if (!_isDirectory[id])
        {
            return false;
        }
        var end = _childStart[id] + _childCount[id];
        for (var index = _childStart[id]; index < end; index++)
        {
            if (!_deleted[index])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Enfants non supprimés, du plus lourd au plus léger.</summary>
    public IEnumerable<int> ChildrenBySize(int id)
    {
        if (!_isDirectory[id])
        {
            yield break;
        }
        var end = _childStart[id] + _childCount[id];
        for (var index = _childStart[id]; index < end; index++)
        {
            var child = _bySize[index];
            if (!_deleted[child])
            {
                yield return child;
            }
        }
    }

    public List<int> Children(int id, StorageSortKey key, bool descending)
    {
        var children = ChildrenBySize(id).ToList();
        if (key == StorageSortKey.Size)
        {
            if (!descending)
            {
                children.Reverse();
            }
            return children;
        }
        Comparison<int> comparison = key switch
        {
            StorageSortKey.Allocated => (left, right) => Allocated(left).CompareTo(Allocated(right)),
            StorageSortKey.Items => (left, right) => Items(left).CompareTo(Items(right)),
            StorageSortKey.Modified => (left, right) => _modified[left].CompareTo(_modified[right]),
            _ => (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(_name[left], _name[right])
        };
        children.Sort(descending ? (left, right) => comparison(right, left) : comparison);
        return children;
    }

    public string GetPath(int id)
    {
        if (id == 0)
        {
            return RootPath;
        }
        var parts = new Stack<string>();
        for (var current = id; current > 0; current = _parent[current])
        {
            parts.Push(_name[current]);
        }
        return Path.Combine([RootPath, .. parts]);
    }

    /// <summary>Chaîne des ancêtres, racine comprise, jusqu'à <paramref name="id"/>.</summary>
    public List<int> Ancestry(int id)
    {
        var chain = new List<int>();
        for (var current = id; current >= 0; current = _parent[current])
        {
            chain.Add(current);
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>Tous les fichiers non supprimés, du plus volumineux au plus petit.</summary>
    public int[] FilesBySize()
    {
        var files = new List<int>(Math.Max(16, (int)Math.Min(int.MaxValue, _files[0])));
        for (var id = 1; id < Count; id++)
        {
            if (!_isDirectory[id] && !_deleted[id] && !IsUnderDeleted(id))
            {
                files.Add(id);
            }
        }
        var array = files.ToArray();
        var keys = Array.ConvertAll(array, id => ulong.MaxValue - _size[id]);
        Array.Sort(keys, array);
        return array;
    }

    private bool IsUnderDeleted(int id)
    {
        for (var current = _parent[id]; current >= 0; current = _parent[current])
        {
            if (_deleted[current])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Retire un élément après sa suppression sur disque : le sous-arbre est marqué, ses tailles
    /// et compteurs sont retirés de chaque ancêtre et des extensions. Renvoie la taille retirée.
    /// </summary>
    public ulong Remove(int id)
    {
        if (id <= 0 || _deleted[id])
        {
            return 0;
        }
        var weight = _weight[id];
        var allocated = _weightAllocated[id];
        var files = _isDirectory[id] ? _files[id] : 1;
        var folders = _isDirectory[id] ? _folders[id] + 1 : 0;
        RemoveFromExtensions(id);
        _deleted[id] = true;
        for (var current = _parent[id]; current >= 0; current = _parent[current])
        {
            _weight[current] -= Math.Min(_weight[current], weight);
            _weightAllocated[current] -= Math.Min(_weightAllocated[current], allocated);
            _files[current] -= files;
            _folders[current] -= folders;
        }
        return weight;
    }

    private void RemoveFromExtensions(int id)
    {
        var stack = new Stack<int>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (_deleted[current])
            {
                continue;
            }
            if (_isDirectory[current])
            {
                var end = _childStart[current] + _childCount[current];
                for (var child = _childStart[current]; child < end; child++)
                {
                    stack.Push(child);
                }
            }
            else if (_extension[current] >= 0)
            {
                var stats = _extensions[_extension[current]];
                stats.Files--;
                stats.Bytes -= Math.Min(stats.Bytes, _weight[current]);
                stats.AllocatedBytes -= Math.Min(stats.AllocatedBytes, _weightAllocated[current]);
            }
        }
    }

    public static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1 || name.Length - dot > 16)
        {
            return NoExtension;
        }
        return name[dot..].ToLowerInvariant();
    }
}
