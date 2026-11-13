namespace Optivance.Core;

public enum CleaningLevel
{
    /// <summary>Sans droits administrateur, sans risque pour Windows ni les documents.</summary>
    Standard,
    /// <summary>Emplacements système : droits administrateur (composant de session).</summary>
    Deep
}

/// <summary>Emplacement nettoyé : dossier (variables d'environnement admises), motif de fichiers, âge minimal.</summary>
public sealed record CleaningTarget(string Folder, string Pattern = "*", bool Recursive = true, int MinimumAgeHours = 0, bool WholeFolderContent = true);

/// <summary>Catégorie de nettoyage. Le contenu supprimé est régénéré par Windows ou les applications.</summary>
public sealed record CleaningCategory(
    string Id,
    string Name,
    string Description,
    CleaningLevel Level,
    IReadOnlyList<CleaningTarget> Targets,
    bool CheckedByDefault = true)
{
    public bool RequiresAdmin => Level == CleaningLevel.Deep;
}

/// <summary>
/// Catalogue fixe : aucune catégorie ne vise des documents personnels. Les identifiants des
/// catégories approfondies sont les seuls acceptés par le composant administrateur, qui en
/// détient sa propre copie des emplacements (aucun chemin n'est transmis).
/// </summary>
public static class CleaningCatalog
{
    /// <summary>Catégorie spéciale traitée par l'API de la Corbeille.</summary>
    public const string RecycleBinId = "recycle-bin";

    public static IReadOnlyList<CleaningCategory> All { get; } =
    [
        new("user-temp", "Fichiers temporaires", "Fichiers temporaires de vos applications (%TEMP%), datant de plus de 24 heures.", CleaningLevel.Standard,
            [new(@"%TEMP%", MinimumAgeHours: 24)]),
        new("browser-cache", "Caches des navigateurs", "Pages et images mises en cache par Edge, Chrome, Brave, Opera et Firefox. Vos mots de passe, favoris et historiques ne sont pas touchés.", CleaningLevel.Standard,
            [
                new(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Cache"),
                new(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Code Cache"),
                new(@"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache"),
                new(@"%LOCALAPPDATA%\Google\Chrome\User Data\*\Code Cache"),
                new(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Cache"),
                new(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Code Cache"),
                new(@"%LOCALAPPDATA%\Opera Software\Opera Stable\Cache"),
                new(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2")
            ]),
        new("shader-cache", "Caches de shaders", "Shaders compilés par DirectX et les pilotes graphiques ; recréés automatiquement (premier lancement d’un jeu un peu plus long).", CleaningLevel.Standard,
            [new(@"%LOCALAPPDATA%\D3DSCache"), new(@"%LOCALAPPDATA%\NVIDIA\DXCache"), new(@"%LOCALAPPDATA%\NVIDIA\GLCache"), new(@"%LOCALAPPDATA%\AMD\DxCache")], CheckedByDefault: false),
        new("thumbnails", "Miniatures de l’Explorateur", "Aperçus d’images et de vidéos ; recréés à l’affichage des dossiers.", CleaningLevel.Standard,
            [new(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "thumbcache_*.db", Recursive: false, WholeFolderContent: false)], CheckedByDefault: false),
        new("user-crash", "Rapports d’erreurs et vidages d’applications", "Rapports d’erreurs Windows et vidages mémoire de vos applications.", CleaningLevel.Standard,
            [new(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive"), new(@"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue"), new(@"%LOCALAPPDATA%\CrashDumps")]),
        new(RecycleBinId, "Corbeille", "Vider la Corbeille de tous les disques (définitif).", CleaningLevel.Standard, [], CheckedByDefault: false),

        new("windows-temp", "Fichiers temporaires de Windows", "Dossier Windows\\Temp, fichiers de plus de 24 heures.", CleaningLevel.Deep,
            [new(@"%WINDIR%\Temp", MinimumAgeHours: 24)]),
        new("windows-update", "Cache de Windows Update", "Mises à jour déjà installées et téléchargements terminés ; Windows les retélécharge si nécessaire.", CleaningLevel.Deep,
            [new(@"%WINDIR%\SoftwareDistribution\Download", MinimumAgeHours: 24)]),
        new("delivery-optimization", "Optimisation de distribution", "Copies de mises à jour partagées avec d’autres PC.", CleaningLevel.Deep,
            [new(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")]),
        new("memory-dumps", "Vidages mémoire système", "Fichiers créés lors d’écrans bleus (MEMORY.DMP, Minidump) ; inutiles sauf pour un diagnostic en cours.", CleaningLevel.Deep,
            [new(@"%WINDIR%", "MEMORY.DMP", Recursive: false, WholeFolderContent: false), new(@"%WINDIR%\Minidump"), new(@"%WINDIR%\LiveKernelReports", "*.dmp")]),
        new("system-crash", "Rapports d’erreurs système", "Rapports d’erreurs Windows de tous les comptes.", CleaningLevel.Deep,
            [new(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive"), new(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue")]),
        new("windows-logs", "Anciens journaux de Windows", "Journaux d’installation et de maintenance de plus de 7 jours.", CleaningLevel.Deep,
            [new(@"%WINDIR%\Logs\CBS", "*.log", MinimumAgeHours: 168), new(@"%WINDIR%\Logs\DISM", "*.log", MinimumAgeHours: 168), new(@"%WINDIR%\Panther", "*.log", MinimumAgeHours: 168)], CheckedByDefault: false),
        new("windows-old", "Ancienne installation de Windows", "Dossier Windows.old laissé après une mise à niveau ; le supprimer empêche de revenir à la version précédente.", CleaningLevel.Deep,
            [new(@"%SYSTEMDRIVE%\Windows.old")], CheckedByDefault: false),
    ];

    public static CleaningCategory? Find(string id) => All.FirstOrDefault(category => category.Id == id);

    /// <summary>
    /// Vrai si <paramref name="path"/> est un emplacement que le nettoyage ne doit jamais viser :
    /// racine d'un disque, dossier Windows lui-même, profil utilisateur, Program Files, etc.
    /// (garde-fou contre une variable d'environnement vide ou détournée).
    /// </summary>
    public static bool IsForbiddenRoot(string path)
    {
        string Normalize(string value) => value.TrimEnd('\\', '/').ToUpperInvariant();
        var candidate = Normalize(path);
        if (candidate.Length <= 3)
        {
            return true;
        }
        string?[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
        ];
        return roots.Any(root => !string.IsNullOrEmpty(root) && Normalize(root) == candidate);
    }
}
