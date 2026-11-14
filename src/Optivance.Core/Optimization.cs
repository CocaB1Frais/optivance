using System.Text.Json;

namespace Optivance.Core;

public enum TweakRisk
{
    /// <summary>Sans conséquence notable, réversible.</summary>
    Safe,
    /// <summary>Effet visible possible sur certaines fonctions : conséquences affichées avant d'appliquer.</summary>
    Moderate,
    /// <summary>Peut casser une fonction utilisée : avertissement renforcé avant d'appliquer.</summary>
    Risky
}

/// <summary>Service Windows modifiable (même liste que le composant administrateur).</summary>
public sealed record ServiceTweak(string Name, string Title, string Description, TweakRisk Risk, string Consequence);

/// <summary>Tâche planifiée modifiable (identifiant connu du composant administrateur).</summary>
public sealed record TaskTweak(string Id, string Path, string Title, TweakRisk Risk, string Consequence);

public static class OptimizationCatalog
{
    public static IReadOnlyList<ServiceTweak> Services { get; } =
    [
        new("DiagTrack", "Télémétrie Windows", "Envoi de données de diagnostic à Microsoft.", TweakRisk.Safe, "Moins de données envoyées ; aucune fonction utilisateur concernée."),
        new("dmwappushservice", "Routage des messages WAP", "Utilisé par la télémétrie et quelques fonctions de gestion d’entreprise.", TweakRisk.Moderate, "PC gérés par une entreprise (Intune/MDM) : la gestion à distance peut ne plus fonctionner."),
        new("SysMain", "SysMain (préchargement)", "Précharge en mémoire les applications fréquentes.", TweakRisk.Moderate, "Sur disque dur mécanique, les applications peuvent démarrer plus lentement. Sans effet notable sur SSD."),
        new("WSearch", "Windows Search (indexation)", "Indexe les fichiers pour la recherche.", TweakRisk.Risky, "La recherche du menu Démarrer, de l’Explorateur et d’Outlook devient lente ou incomplète."),
        new("Spooler", "Spouleur d’impression", "Gère les imprimantes.", TweakRisk.Risky, "Plus aucune impression possible (y compris « Imprimer en PDF ») tant que le service est désactivé."),
        new("Fax", "Télécopie", "Envoi et réception de fax.", TweakRisk.Safe, "Le fax Windows ne fonctionne plus."),
        new("MapsBroker", "Cartes téléchargées", "Mise à jour des cartes hors connexion.", TweakRisk.Safe, "L’application Cartes ne met plus à jour les cartes hors connexion."),
        new("WerSvc", "Rapport d’erreurs Windows", "Envoie les rapports de plantage.", TweakRisk.Moderate, "Les plantages ne sont plus signalés ; Windows propose moins de solutions automatiques."),
        new("XblAuthManager", "Xbox Live : authentification", "Connexion aux services Xbox.", TweakRisk.Moderate, "Jeux Xbox / Game Pass : connexion et succès indisponibles."),
        new("XblGameSave", "Xbox Live : sauvegardes", "Synchronise les sauvegardes de jeux Xbox.", TweakRisk.Moderate, "Jeux Xbox / Game Pass : les sauvegardes ne sont plus synchronisées dans le cloud."),
        new("XboxNetApiSvc", "Xbox Live : réseau", "Fonctions réseau des jeux Xbox.", TweakRisk.Moderate, "Jeux Xbox en ligne : le multijoueur peut ne plus fonctionner."),
        new("XboxGipSvc", "Accessoires Xbox", "Gestion des accessoires Xbox (mise à jour des manettes).", TweakRisk.Moderate, "Mises à jour et réglages des manettes Xbox indisponibles (la manette reste utilisable)."),
        new("RetailDemo", "Mode démonstration", "Mode vitrine des magasins.", TweakRisk.Safe, "Aucune conséquence pour un usage personnel."),
        new("lfsvc", "Géolocalisation", "Fournit la position aux applications.", TweakRisk.Moderate, "Météo, Cartes et « Localiser mon appareil » ne connaissent plus votre position."),
    ];

    public static IReadOnlyList<TaskTweak> Tasks { get; } =
    [
        new("compat-appraiser", @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", "Évaluation de compatibilité (télémétrie)", TweakRisk.Safe, "Moins d’activité disque périodique ; la compatibilité est encore vérifiée lors des mises à niveau."),
        new("program-data-updater", @"\Microsoft\Windows\Application Experience\ProgramDataUpdater", "Inventaire des programmes (télémétrie)", TweakRisk.Safe, "Aucune fonction utilisateur concernée."),
        new("ceip-consolidator", @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", "Programme d’amélioration (CEIP)", TweakRisk.Safe, "Aucune fonction utilisateur concernée."),
        new("ceip-usb", @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", "Statistiques USB (CEIP)", TweakRisk.Safe, "Aucune fonction utilisateur concernée."),
        new("autochk-proxy", @"\Microsoft\Windows\Autochk\Proxy", "Collecte Autochk (télémétrie)", TweakRisk.Safe, "Aucune fonction utilisateur concernée."),
        new("disk-diagnostic", @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", "Collecte de diagnostic disque", TweakRisk.Moderate, "Windows n’envoie plus de diagnostic disque ; les alertes SMART locales restent actives."),
        new("feedback-dm", @"\Microsoft\Windows\Feedback\Siuf\DmClient", "Commentaires Windows", TweakRisk.Safe, "Windows ne demande plus votre avis."),
    ];

    /// <summary>
    /// Valeur StartupApproved (format Windows / Gestionnaire des tâches) : premier octet pair
    /// (02, 06) = activé, impair (03, 07) = désactivé ; absente = activé.
    /// </summary>
    public static bool IsStartupApproved(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] StartupApprovedValue(bool enabled, DateTime nowUtc)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.GetBytes(nowUtc.ToFileTimeUtc()).CopyTo(value, 4);
        }
        return value;
    }

    /// <summary>Type de démarrage d'un service à partir du registre (Start, DelayedAutostart).</summary>
    public static string StartMode(int? start, int? delayed) => start switch
    {
        2 when delayed == 1 => "delayed-auto",
        2 => "auto",
        3 => "demand",
        4 => "disabled",
        _ => "unknown"
    };

    public static string StartModeLabel(string mode) => mode switch
    {
        "auto" => "Automatique",
        "delayed-auto" => "Automatique (différé)",
        "demand" => "Manuel",
        "disabled" => "Désactivé",
        _ => "Inconnu"
    };
}

/// <summary>
/// Valeurs d'origine des réglages modifiés par OPTIVANCE (%LOCALAPPDATA%\OPTIVANCE\
/// optimization-backup.json). Seule la première valeur d'origine est conservée, pour que
/// « Restaurer » revienne toujours à l'état d'avant OPTIVANCE.
/// </summary>
public sealed class OptimizationBackup
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values;

    public OptimizationBackup(string? path = null)
    {
        _path = path ?? Path.Combine(AppSettings.Folder, "optimization-backup.json");
        try
        {
            _values = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            _values = [];
        }
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool TryGet(string key, out string value) => _values.TryGetValue(key, out value!);

    /// <summary>Enregistre la valeur d'origine si elle ne l'est pas déjà.</summary>
    public void Remember(string key, string original)
    {
        if (_values.TryAdd(key, original))
        {
            Save();
        }
    }

    public void Forget(string key)
    {
        if (_values.Remove(key))
        {
            Save();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
    }
}
