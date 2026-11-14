using System.Runtime.InteropServices;
using Microsoft.Win32;
using Optivance.Core;

namespace Optivance.Infrastructure.Monitoring;

/// <summary>
/// Surveillance du système sans pilote : compteurs Windows (PDH), mémoire (GlobalMemoryStatusEx),
/// NVIDIA (NVML si présente). L'échantillonnage ne tourne que tant qu'il y a des abonnés (page
/// Monitoring ou widget visibles) ; l'historique reste en mémoire (D-18).
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private const string CpuUtility = @"\Processor Information(_Total)\% Processor Utility";
    private const string CpuPerformance = @"\Processor Information(_Total)\% Processor Performance";
    private const string CpuBaseFrequency = @"\Processor Information(_Total)\Processor Frequency";
    private const string Thermal = @"\Thermal Zone Information(*)\Temperature";
    private const string GpuEngines = @"\GPU Engine(*)\Utilization Percentage";
    private const string GpuDedicated = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string DiskRead = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    private const string DiskWrite = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";
    private const string DiskIdle = @"\PhysicalDisk(_Total)\% Idle Time";
    private const string NetDown = @"\Network Interface(*)\Bytes Received/sec";
    private const string NetUp = @"\Network Interface(*)\Bytes Sent/sec";
    private const string Processes = @"\System\Processes";

    private readonly object _gate = new();
    private readonly List<Action<SystemSnapshot>> _subscribers = [];
    private PdhQuery? _pdh;
    private NvmlGpu? _nvidia;
    private AdlGpu? _amd;
    private bool _nvidiaProbed;
    private Timer? _timer;
    private int _intervalMilliseconds = 1000;
    private int _sampling;

    public static SystemMonitor Shared { get; } = new();

    public string CpuName { get; } = ReadCpuName();
    public SystemSnapshot? Last { get; private set; }

    /// <summary>Fournit la source des FPS (composant de session) ; null = indisponible.</summary>
    public Func<(double? Fps, string? Process)>? FpsSource { get; set; }

    public int IntervalMilliseconds
    {
        get => _intervalMilliseconds;
        set
        {
            _intervalMilliseconds = Math.Clamp(value, 250, 10_000);
            lock (_gate)
            {
                _timer?.Change(_intervalMilliseconds, _intervalMilliseconds);
            }
        }
    }

    /// <summary>S'abonne aux mesures ; disposer l'objet renvoyé arrête l'abonnement.</summary>
    public IDisposable Subscribe(Action<SystemSnapshot> handler)
    {
        lock (_gate)
        {
            _subscribers.Add(handler);
            if (_timer is null)
            {
                _timer = new Timer(_ => Sample(), null, 0, _intervalMilliseconds);
            }
        }
        return new Subscription(this, handler);
    }

    private void Unsubscribe(Action<SystemSnapshot> handler)
    {
        lock (_gate)
        {
            _subscribers.Remove(handler);
            if (_subscribers.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    private void Sample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1)
        {
            return;
        }
        try
        {
            var snapshot = Measure();
            Last = snapshot;
            Action<SystemSnapshot>[] handlers;
            lock (_gate)
            {
                handlers = [.. _subscribers];
            }
            foreach (var handler in handlers)
            {
                handler(snapshot);
            }
        }
        finally
        {
            Volatile.Write(ref _sampling, 0);
        }
    }

    /// <summary>Diagnostic : lecture directe de la carte AMD (même si une carte NVIDIA est présente).</summary>
    public static string ProbeAmd()
    {
        var log = new System.Text.StringBuilder();
        using var gpu = AdlGpu.TryOpen(line => log.AppendLine(line));
        log.AppendLine(gpu?.Read() is { } sensors ? sensors.ToString() : "aucune carte AMD lisible");
        return log.ToString();
    }

    private SystemSnapshot Measure()
    {
        if (_pdh is null)
        {
            _pdh = new PdhQuery();
            foreach (var path in new[] { CpuUtility, CpuPerformance, CpuBaseFrequency, Thermal, GpuEngines, GpuDedicated, DiskRead, DiskWrite, DiskIdle, NetDown, NetUp, Processes })
            {
                _pdh.Add(path);
            }
            _pdh.Collect(); // Les compteurs de débit demandent deux relevés.
            Thread.Sleep(100);
        }
        if (!_nvidiaProbed)
        {
            _nvidiaProbed = true;
            _nvidia = NvmlGpu.TryOpen();
            // Carte AMD : seulement si aucune carte NVIDIA n'est lue (une seule carte affichée).
            _amd = _nvidia is null ? AdlGpu.TryOpen() : null;
        }
        _pdh.Collect();

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        var performance = _pdh.Value(CpuPerformance);
        var baseFrequency = _pdh.Value(CpuBaseFrequency);
        var idle = _pdh.Value(DiskIdle);
        var nvidia = _nvidia;
        var nvidiaMemory = nvidia?.Memory;
        var dedicated = _pdh.Values(GpuDedicated).Sum(item => item.Value);
        var fps = FpsSource?.Invoke();
        var amd = _amd?.Read();

        return new SystemSnapshot
        {
            CpuPercent = _pdh.Value(CpuUtility) is { } utility ? Math.Clamp(utility, 0, 100) : null,
            CpuFrequencyMhz = performance is { } p && baseFrequency is { } b ? b * p / 100 : baseFrequency,
            ThermalCelsius = MonitoringMath.HottestZoneCelsius(_pdh.Values(Thermal).Select(item => item.Value)) ?? amd?.ProcessorCelsius,
            GpuName = nvidia?.Name ?? (_amd is null ? null : "Carte graphique AMD"),
            GpuPercent = nvidia?.UtilizationPercent ?? amd?.ActivityPercent ?? MonitoringMath.Gpu3dPercent(_pdh.Values(GpuEngines)),
            GpuCelsius = nvidia?.TemperatureCelsius ?? amd?.TemperatureCelsius,
            GpuWatts = nvidia?.PowerWatts ?? amd?.PowerWatts,
            GpuClockMhz = nvidia?.CoreClockMhz ?? amd?.ClockMhz,
            GpuMemoryUsed = nvidiaMemory?.Used ?? (dedicated > 0 ? (ulong)dedicated : null),
            GpuMemoryTotal = nvidiaMemory?.Total,
            MemoryTotal = memory.TotalPhys,
            MemoryUsed = memory.TotalPhys - memory.AvailPhys,
            DiskReadBytesPerSecond = _pdh.Value(DiskRead),
            DiskWriteBytesPerSecond = _pdh.Value(DiskWrite),
            DiskActivePercent = idle is { } i ? Math.Clamp(100 - i, 0, 100) : null,
            NetworkDownBytesPerSecond = MonitoringMath.NetworkTotal(_pdh.Values(NetDown)),
            NetworkUpBytesPerSecond = MonitoringMath.NetworkTotal(_pdh.Values(NetUp)),
            Processes = _pdh.Value(Processes) is { } count ? (int)count : null,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            Fps = fps?.Fps,
            FpsProcess = fps?.Process
        };
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processeur";
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "Processeur";
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
        _pdh?.Dispose();
        _nvidia?.Dispose();
        _amd?.Dispose();
    }

    private sealed class Subscription(SystemMonitor monitor, Action<SystemSnapshot> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                monitor.Unsubscribe(handler);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
