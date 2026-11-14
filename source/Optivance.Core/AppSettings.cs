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
    /// <summary>L'entrée du clic droit du Bureau a déjà été ajoutée une fois (premier lancement) : ne plus la rajouter.</summary>
    public bool DesktopMenuOffered { get; set; }
    /// <summary>Vérifier les mises à jour sur GitHub au lancement (au plus une fois par jour).</summary>
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

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
    public bool WidgetShowGpuPower { get; set; } = true;
    public bool WidgetShowDisk { get; set; }
    /// <summary>Widget réduit : une seule ligne, sans titre.</summary>
    public bool WidgetCompact { get; set; }
    /// <summary>Le widget apparaît seul quand un jeu plein écran est au premier plan, et disparaît après.</summary>
    public bool WidgetAutoShowInGames { get; set; } = true;

    // Alertes (notification Windows, couleur dans le widget, son)
    public bool AlertCpuTemperature { get; set; } = true;
    public int AlertCpuCelsius { get; set; } = 90;
    public bool AlertGpuTemperature { get; set; } = true;
    public int AlertGpuCelsius { get; set; } = 85;
    public bool AlertDiskSpace { get; set; } = true;
    public int AlertDiskFreePercent { get; set; } = 10;
    public bool AlertNotification { get; set; } = true;
    public bool AlertSound { get; set; } = true;
    public string WidgetToggleHotkey { get; set; } = "Ctrl+Alt+W";
    public string WidgetClickThroughHotkey { get; set; } = "Ctrl+Alt+X";

    // Historique
    public int HistoryRetentionDays { get; set; } = 180;

    [JsonIgnore]
    public static string Folder =>
        // OPTIVANCE_DATA_DIR : dossier de données de test (essais isolés, sans toucher aux vraies données).
        Environment.GetEnvironmentVariable("OPTIVANCE_DATA_DIR") is { Length: > 0 } test ? test
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OPTIVANCE");

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
        AlertCpuCelsius = Math.Clamp(AlertCpuCelsius, 50, 110);
        AlertGpuCelsius = Math.Clamp(AlertGpuCelsius, 50, 110);
        AlertDiskFreePercent = Math.Clamp(AlertDiskFreePercent, 1, 50);
        WidgetToggleHotkey = string.IsNullOrWhiteSpace(WidgetToggleHotkey) ? "Ctrl+Alt+W" : WidgetToggleHotkey.Trim();
        WidgetClickThroughHotkey = string.IsNullOrWhiteSpace(WidgetClickThroughHotkey) ? "Ctrl+Alt+X" : WidgetClickThroughHotkey.Trim();
    }
}
