using System.Runtime.InteropServices;
using Optivance.Core;

namespace Optivance.Infrastructure.Monitoring;

/// <summary>
/// Carte AMD via ADL (atiadlxx.dll installée avec le pilote AMD, chargée dynamiquement ;
/// non embarquée dans OPTIVANCE ; en-têtes ADL sous licence MIT, déclarations réécrites ici).
/// Overdrive 8 (RX 5000 et plus récentes) : PMLog ; Overdrive N (RX 400/500/Vega) : température.
/// Absente ou en échec : les valeurs restent « indisponibles ».
/// </summary>
internal sealed class AdlGpu : IDisposable
{
    private const int AdlOk = 0;
    private const int MaxSensors = 256;
    // ADLPMLogDataOutput = int size + 256 × (int supported, int value).
    private const int PmLogBytes = 4 + MaxSensors * 8;

    private static readonly byte[] Zeros = new byte[PmLogBytes + 1024];
    private static readonly MallocCallback Allocate = size => Marshal.AllocHGlobal(size);

    private readonly IntPtr _library;
    private readonly IntPtr _context;
    private readonly int _adapter;
    private readonly int _overdriveVersion;
    private readonly QueryPmLog? _queryPmLog;
    private readonly OverdriveNTemperature? _temperatureN;
    private readonly ContextDestroy _destroy;
    private readonly IntPtr _buffer = Marshal.AllocHGlobal(PmLogBytes + 1024);

    private AdlGpu(IntPtr library, IntPtr context, int adapter, int version, QueryPmLog? queryPmLog, OverdriveNTemperature? temperatureN, ContextDestroy destroy)
    {
        _library = library;
        _context = context;
        _adapter = adapter;
        _overdriveVersion = version;
        _queryPmLog = queryPmLog;
        _temperatureN = temperatureN;
        _destroy = destroy;
    }

    public static AdlGpu? TryOpen(Action<string>? trace = null)
    {
        var path = Path.Combine(Environment.SystemDirectory, "atiadlxx.dll");
        if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
        {
            return null;
        }
        try
        {
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            T? Optional<T>(string name) where T : Delegate =>
                NativeLibrary.TryGetExport(library, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;

            var create = Function<ContextCreate>("ADL2_Main_Control_Create");
            var destroy = Function<ContextDestroy>("ADL2_Main_Control_Destroy");
            var created = create(Allocate, 1, out var context);
            trace?.Invoke($"Main_Control_Create={created}");
            if (created != AdlOk || context == IntPtr.Zero)
            {
                NativeLibrary.Free(library);
                return null;
            }
            var count = Function<AdapterCount>("ADL2_Adapter_NumberOfAdapters_Get");
            var active = Function<AdapterActive>("ADL2_Adapter_Active_Get");
            var caps = Function<OverdriveCaps>("ADL2_Overdrive_Caps");
            var queryPmLog = Optional<QueryPmLog>("ADL2_New_QueryPMLogData_Get");
            var temperatureN = Optional<OverdriveNTemperature>("ADL2_OverdriveN_Temperature_Get");
            var counted = count(context, out var adapters);
            trace?.Invoke($"NumberOfAdapters={counted} ({adapters}) PMLog={queryPmLog is not null} OverdriveN={temperatureN is not null}");
            if (counted == AdlOk)
            {
                for (var index = 0; index < Math.Min(adapters, 64); index++)
                {
                    var activeResult = active(context, index, out var isActive);
                    var capsResult = caps(context, index, out var supported, out var enabled, out var version);
                    trace?.Invoke($"  adaptateur {index} : actif={activeResult}/{isActive} overdrive={capsResult} pris en charge={supported} activé={enabled} version={version}");
                    // Les capteurs sont tentés même si Overdrive n'est pas « pris en charge » (réglages) :
                    // la lecture PMLog en est indépendante sur certaines cartes.
                    var gpu = new AdlGpu(library, context, index, capsResult == AdlOk ? version : 8, queryPmLog, temperatureN, destroy);
                    var reading = gpu.Read();
                    trace?.Invoke($"  lecture : {reading?.ToString() ?? "échec"}");
                    if (gpu.LastRaw is { } raw) { trace?.Invoke($"  bruts : {raw}"); }
                    if (reading is { TemperatureCelsius: not null })
                    {
                        return gpu;
                    }
                    Marshal.FreeHGlobal(gpu._buffer);
                }
            }
            // La bibliothèque reste chargée : la décharger pendant que le pilote AMD termine ses fils fait planter le processus.
            destroy(context);
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            NativeLibrary.Free(library);
            return null;
        }
    }

    /// <summary>Capteurs bruts de la dernière lecture PMLog (diagnostic).</summary>
    public string? LastRaw { get; private set; }

    /// <summary>Dernière lecture (null si la carte ne répond pas).</summary>
    public AmdGpuSensors? Read()
    {
        if (_overdriveVersion >= 8 && _queryPmLog is not null)
        {
            Marshal.Copy(Zeros, 0, _buffer, Zeros.Length);
            Marshal.WriteInt32(_buffer, PmLogBytes);
            if (_queryPmLog(_context, _adapter, _buffer) == AdlOk)
            {
                var supported = new int[MaxSensors];
                var values = new int[MaxSensors];
                for (var i = 0; i < MaxSensors; i++)
                {
                    supported[i] = Marshal.ReadInt32(_buffer, 4 + i * 8);
                    values[i] = Marshal.ReadInt32(_buffer, 8 + i * 8);
                }
                LastRaw = string.Join(" ", Enumerable.Range(0, MaxSensors).Where(i => supported[i] != 0).Select(i => $"{i}={values[i]}"));
                return MonitoringMath.AmdPmLog(supported, values);
            }
        }
        if (_temperatureN is not null && _temperatureN(_context, _adapter, 1 /* bord de la puce */, out var milli) == AdlOk)
        {
            return MonitoringMath.AmdPmLog([], []) with { TemperatureCelsius = MonitoringMath.PlausibleCelsius(milli / 1000.0) };
        }
        return null;
    }

    public void Dispose()
    {
        _destroy(_context);
        Marshal.FreeHGlobal(_buffer);
        // _library volontairement non déchargée (voir TryOpen).
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr MallocCallback(int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ContextCreate(MallocCallback callback, int connectedOnly, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ContextDestroy(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterCount(IntPtr context, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterActive(IntPtr context, int adapter, out int active);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OverdriveCaps(IntPtr context, int adapter, out int supported, out int enabled, out int version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueryPmLog(IntPtr context, int adapter, IntPtr output);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int OverdriveNTemperature(IntPtr context, int adapter, int type, out int milliCelsius);
}
