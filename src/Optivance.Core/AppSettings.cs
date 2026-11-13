using System.Text.Json;
using System.Text.Json.Serialization;

namespace Optivance.Core;

/// <summary>
/// Préférences de l'utilisateur, enregistrées localement (%LOCALAPPDATA%\OPTIVANCE\settings.json).
/// Aucune donnée ne quitte la machine. Une valeur absente ou illisible reprend sa valeur par défaut.
/// </summary>
public sealed class AppSettings
{
    // Général
    public bool RequestElevationAtStartup { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartHiddenInTray { get; set; }

    // Storage
    public bool ConfirmDeletion { get; set; } = true;
    public bool PreferFastScan { get; set; } = true;

    // Monitoring
    public int MonitoringRefreshMilliseconds { get; set; } = 1000;

    // Widget
    public bool WidgetVisible { get; set; }
    public double WidgetOpacity { get; set; } = 0.82;
    public double WidgetLeft { get; set; } = double.NaN;
    public double WidgetTop { get; set; } = double.NaN;
    public bool WidgetClickThrough { get; set; }
    public bool WidgetShowCpu { get; set; } = true;
    public bool WidgetShowGpu { get; set; } = true;
    public bool WidgetShowMemory { get; set; } = true;
    public bool WidgetShowTemperatures { get; set; } = true;
    public bool WidgetShowFps { get; set; } = true;
    public bool WidgetShowNetwork { get; set; }
    public string WidgetToggleHotkey { get; set; } = "Ctrl+Alt+W";
    public string WidgetClickThroughHotkey { get; set; } = "Ctrl+Alt+X";

    // Historique
    public int HistoryRetentionDays { get; set; } = 180;

    [JsonIgnore]
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OPTIVANCE");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? FilePath;
            if (File.Exists(file))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), Options) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fichier illisible : valeurs par défaut (il sera réécrit à la prochaine sauvegarde).
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        Normalize();
        var file = path ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
        File.Move(temporary, file, overwrite: true);
    }

    private void Normalize()
    {
        MonitoringRefreshMilliseconds = Math.Clamp(MonitoringRefreshMilliseconds, 250, 10_000);
        WidgetOpacity = Math.Clamp(double.IsFinite(WidgetOpacity) ? WidgetOpacity : 0.82, 0.25, 1.0);
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 3650);
        WidgetToggleHotkey = string.IsNullOrWhiteSpace(WidgetToggleHotkey) ? "Ctrl+Alt+W" : WidgetToggleHotkey.Trim();
        WidgetClickThroughHotkey = string.IsNullOrWhiteSpace(WidgetClickThroughHotkey) ? "Ctrl+Alt+X" : WidgetClickThroughHotkey.Trim();
    }
}
