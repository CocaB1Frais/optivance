using System.Runtime.InteropServices;
using System.Text;

namespace Optivance.Infrastructure.Monitoring;

/// <summary>
/// Carte NVIDIA via NVML (nvml.dll installée avec le pilote NVIDIA, chargée dynamiquement ;
/// non embarquée dans OPTIVANCE). Absente ou en échec : les valeurs sont « indisponibles ».
/// </summary>
internal sealed class NvmlGpu : IDisposable
{
    private readonly IntPtr _library;
    private readonly IntPtr _device;
    private readonly GetUInt _temperature;
    private readonly GetUIntDirect _power;
    private readonly GetUtilization _utilization;
    private readonly GetMemory _memory;
    private readonly GetClock _clock;
    private readonly Action _shutdown;

    private NvmlGpu(IntPtr library, IntPtr device, string name, Delegate[] functions, Action shutdown)
    {
        _library = library;
        _device = device;
        Name = name;
        _temperature = (GetUInt)functions[0];
        _power = (GetUIntDirect)functions[1];
        _utilization = (GetUtilization)functions[2];
        _memory = (GetMemory)functions[3];
        _clock = (GetClock)functions[4];
        _shutdown = shutdown;
    }

    public string Name { get; }

    public static NvmlGpu? TryOpen()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll")
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
            {
                continue;
            }
            try
            {
                T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
                var init = Function<NoArgs>("nvmlInit_v2");
                if (init() != 0)
                {
                    NativeLibrary.Free(library);
                    continue;
                }
                var shutdown = Function<NoArgs>("nvmlShutdown");
                if (Function<GetHandle>("nvmlDeviceGetHandleByIndex_v2")(0, out var device) != 0)
                {
                    shutdown();
                    NativeLibrary.Free(library);
                    continue;
                }
                var name = new StringBuilder(96);
                Function<GetName>("nvmlDeviceGetName")(device, name, (uint)name.Capacity);
                return new NvmlGpu(library, device, name.ToString(),
                [
                    Function<GetUInt>("nvmlDeviceGetTemperature"),
                    Function<GetUIntDirect>("nvmlDeviceGetPowerUsage"),
                    Function<GetUtilization>("nvmlDeviceGetUtilizationRates"),
                    Function<GetMemory>("nvmlDeviceGetMemoryInfo"),
                    Function<GetClock>("nvmlDeviceGetClockInfo")
                ], () => shutdown());
            }
            catch (EntryPointNotFoundException)
            {
                NativeLibrary.Free(library);
            }
        }
        return null;
    }

    public double? TemperatureCelsius => _temperature(_device, 0, out var value) == 0 ? value : null;
    public double? PowerWatts => _power(_device, out var milliwatts) == 0 ? milliwatts / 1000.0 : null;
    public double? UtilizationPercent => _utilization(_device, out var rates) == 0 ? rates.Gpu : null;
    public (ulong Used, ulong Total)? Memory => _memory(_device, out var memory) == 0 ? (memory.Used, memory.Total) : null;
    /// <summary>Fréquence du cœur graphique (MHz).</summary>
    public double? CoreClockMhz => _clock(_device, 0, out var mhz) == 0 ? mhz : null;

    public void Dispose()
    {
        _shutdown();
        NativeLibrary.Free(_library);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHandle(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int GetName(IntPtr device, StringBuilder name, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUInt(IntPtr device, int sensor, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUIntDirect(IntPtr device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetUtilization(IntPtr device, out Utilization value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetMemory(IntPtr device, out MemoryInfo value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetClock(IntPtr device, int type, out uint value);
}
