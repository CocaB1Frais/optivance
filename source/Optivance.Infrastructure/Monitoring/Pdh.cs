using System.Runtime.InteropServices;

namespace Optivance.Infrastructure.Monitoring;

/// <summary>
/// Compteurs de performance Windows (PDH, pdh.dll) : API système documentée, sans pilote ni
/// droits administrateur. Les noms anglais sont utilisés (indépendants de la langue de Windows).
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const uint PdhMoreData = 0x800007D2;
    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = [];

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
        {
            _query = IntPtr.Zero;
        }
    }

    /// <summary>Ajoute un compteur (éventuellement avec joker) ; faux s'il n'existe pas sur ce PC.</summary>
    public bool Add(string path)
    {
        if (_query == IntPtr.Zero || _counters.ContainsKey(path))
        {
            return _counters.ContainsKey(path);
        }
        if (PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter) != 0)
        {
            return false;
        }
        _counters[path] = counter;
        return true;
    }

    public void Collect()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCollectQueryData(_query);
        }
    }

    public double? Value(string path)
    {
        if (!_counters.TryGetValue(path, out var counter))
        {
            return null;
        }
        return PdhGetFormattedCounterValue(counter, PdhFmtDouble | PdhFmtNoCap100, out _, out var value) == 0 && value.Status == 0
            ? value.DoubleValue
            : null;
    }

    /// <summary>Valeurs de chaque instance d'un compteur à joker (nom d'instance → valeur).</summary>
    public List<(string Instance, double Value)> Values(string path)
    {
        var result = new List<(string, double)>();
        if (!_counters.TryGetValue(path, out var counter))
        {
            return result;
        }
        uint size = 0, count = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, ref count, IntPtr.Zero);
        if (status != PdhMoreData || size == 0)
        {
            return result;
        }
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, ref count, buffer) != 0)
            {
                return result;
            }
            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + index * itemSize);
                if (item.Value.Status == 0)
                {
                    result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.DoubleValue));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr Name;
        public PdhFmtCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
