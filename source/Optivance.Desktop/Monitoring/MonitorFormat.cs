using Optivance.Core;
using Optivance.Core.Storage;

namespace Optivance.Desktop.Monitoring;

/// <summary>Textes d'affichage des mesures (« indisponible » quand Windows ne fournit pas la valeur).</summary>
internal static class MonitorFormat
{
    public const string Unavailable = "indisponible";
    private static readonly System.Globalization.CultureInfo French = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    public static string Percent(double? value) => value is { } v ? v.ToString("0", French) + " %" : Unavailable;
    public static string Celsius(double? value) => value is { } v ? v.ToString("0", French) + " °C" : Unavailable;
    public static string Watts(double? value) => value is { } v ? v.ToString("0", French) + " W" : Unavailable;
    public static string Ghz(double? mhz) => mhz is { } v ? (v / 1000).ToString("0.00", French) + " GHz" : Unavailable;
    public static string Mhz(double? mhz) => mhz is { } v ? v.ToString("0", French) + " MHz" : Unavailable;
    public static string Rate(double? bytesPerSecond) => bytesPerSecond is { } v ? StorageFormat.Bytes((ulong)Math.Max(0, v)) + "/s" : Unavailable;
    public static string Fps(double? value) => value is { } v ? v.ToString("0", French) : "—";

    public static string Memory(ulong? used, ulong? total) =>
        used is { } u ? total is { } t && t > 0 ? $"{StorageFormat.Bytes(u)} / {StorageFormat.Bytes(t)}" : StorageFormat.Bytes(u) : Unavailable;

    public static string Uptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays} j {uptime.Hours} h {uptime.Minutes:00} min" : $"{uptime.Hours} h {uptime.Minutes:00} min";
}
