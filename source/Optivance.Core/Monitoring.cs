namespace Optivance.Core;

/// <summary>Mesure instantanée du système (valeurs null = indisponibles sur ce PC).</summary>
public sealed record SystemSnapshot
{
    public DateTime Time { get; init; } = DateTime.Now;
    public double? CpuPercent { get; init; }
    public double? CpuFrequencyMhz { get; init; }
    /// <summary>Température des zones thermiques ACPI (souvent proche du processeur), °C.</summary>
    public double? ThermalCelsius { get; init; }
    public string? GpuName { get; init; }
    public double? GpuPercent { get; init; }
    public double? GpuCelsius { get; init; }
    public double? GpuWatts { get; init; }
    public double? GpuClockMhz { get; init; }
    public ulong? GpuMemoryUsed { get; init; }
    public ulong? GpuMemoryTotal { get; init; }
    public ulong MemoryUsed { get; init; }
    public ulong MemoryTotal { get; init; }
    public double? DiskReadBytesPerSecond { get; init; }
    public double? DiskWriteBytesPerSecond { get; init; }
    public double? DiskActivePercent { get; init; }
    public double? NetworkDownBytesPerSecond { get; init; }
    public double? NetworkUpBytesPerSecond { get; init; }
    public int? Processes { get; init; }
    public TimeSpan Uptime { get; init; }
    public double? Fps { get; init; }
    public string? FpsProcess { get; init; }
    public double MemoryPercent => MemoryTotal == 0 ? 0 : 100.0 * MemoryUsed / MemoryTotal;
}

/// <summary>Calculs purs sur les compteurs Windows (testables sans matériel).</summary>
public static class MonitoringMath
{
    /// <summary>
    /// Utilisation 3D de la carte graphique à partir des instances « GPU Engine » : somme des moteurs
    /// 3D par adaptateur (identifiant « luid_… »), l'adaptateur le plus chargé, bornée à 100 %.
    /// </summary>
    public static double? Gpu3dPercent(IEnumerable<(string Instance, double Value)> engines)
    {
        var perAdapter = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, value) in engines)
        {
            if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            var end = start < 0 ? -1 : instance.IndexOf("_phys", start, StringComparison.OrdinalIgnoreCase);
            var adapter = start >= 0 && end > start ? instance[start..end] : "";
            perAdapter[adapter] = perAdapter.GetValueOrDefault(adapter) + value;
        }
        return perAdapter.Count == 0 ? null : Math.Clamp(perAdapter.Values.Max(), 0, 100);
    }

    /// <summary>Zones thermiques en kelvins → °C de la zone la plus chaude ; valeurs absurdes ignorées.</summary>
    public static double? HottestZoneCelsius(IEnumerable<double> kelvins)
    {
        var celsius = kelvins.Select(kelvin => kelvin - 273.15).Where(value => value is > 1 and < 130).ToList();
        return celsius.Count == 0 ? null : celsius.Max();
    }

    /// <summary>Somme des interfaces réseau physiques (boucle locale et adaptateurs virtuels d'isatap/teredo exclus).</summary>
    public static double? NetworkTotal(IEnumerable<(string Instance, double Value)> interfaces)
    {
        double total = 0;
        var any = false;
        foreach (var (instance, value) in interfaces)
        {
            if (instance.Contains("Loopback", StringComparison.OrdinalIgnoreCase) || instance.Contains("isatap", StringComparison.OrdinalIgnoreCase) || instance.Contains("Teredo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            total += value;
            any = true;
        }
        return any ? total : null;
    }
    /// <summary>Température plausible (°C) ou null.</summary>
    public static double? PlausibleCelsius(double value) => value is > 1 and < 130 ? value : null;

    /// <summary>
    /// Capteurs PMLog d'une carte AMD (Overdrive 8, indices d'adl_defines.h) : 8 = bord de la puce,
    /// 27 = point chaud, 28 = graphique (puces intégrées), 32 = processeur (APU), 23 = consommation (W),
    /// 1 = fréquence graphique (MHz), 19 = activité (%).
    /// </summary>
    public static AmdGpuSensors AmdPmLog(IReadOnlyList<int> supported, IReadOnlyList<int> values)
    {
        double? Read(int index) => index < supported.Count && index < values.Count && supported[index] != 0 ? values[index] : null;
        var edge = Read(8) is { } e ? PlausibleCelsius(e) : null;
        var hotspot = Read(27) is { } h ? PlausibleCelsius(h) : null;
        var graphics = Read(28) is { } g ? PlausibleCelsius(g) : null;
        var processor = Read(32) is { } p ? PlausibleCelsius(p) : null;
        var power = Read(23) is { } w and > 0 and < 1000 ? w : (double?)null;
        var clock = Read(1) is { } c and > 0 and < 10000 ? c : (double?)null;
        var activity = Read(19) is { } a and >= 0 and <= 100 ? a : (double?)null;
        return new AmdGpuSensors(edge ?? graphics ?? hotspot, hotspot, power, clock, activity, processor);
    }
}

/// <summary>Mesures d'une carte AMD (null = non fournie par la carte).</summary>
public sealed record AmdGpuSensors(double? TemperatureCelsius, double? HotspotCelsius, double? PowerWatts, double? ClockMhz, double? ActivityPercent, double? ProcessorCelsius = null);