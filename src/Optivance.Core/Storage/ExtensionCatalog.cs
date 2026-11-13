namespace Optivance.Core.Storage;

/// <summary>Couleurs (0xRRGGBB) et libellés des extensions. Le type affiché se déduit du nom, pas du contenu.</summary>
public static class ExtensionCatalog
{
    /// <summary>Palette vive pour les extensions les plus volumineuses (rang 0 = la plus lourde).</summary>
    private static readonly int[] Palette =
    [
        0x4F8DF5, 0xF2B33D, 0x3CC48A, 0xE8615A, 0x9B6CF0, 0x2FC4D6, 0xF07CC0, 0x9CCB42,
        0xF08A3C, 0x5E7BF0, 0xD9C43A, 0x45B0A2, 0xC45AD9, 0x6FA8DC, 0xE0A070, 0x7FD17F,
        0xD95F7F, 0x58C9F0, 0xB8A1F0, 0xF0D27C, 0x8AD9C0, 0xF29C9C, 0xA9C46B, 0x7C9BB8
    ];

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dll"] = "Bibliothèque d’application", [".exe"] = "Application", [".sys"] = "Pilote système", [".msi"] = "Paquet d’installation",
        [".mui"] = "Ressources de langue", [".cab"] = "Archive Windows", [".cat"] = "Catalogue de sécurité", [".manifest"] = "Manifeste",
        [".etl"] = "Journal de traces", [".log"] = "Journal", [".evtx"] = "Journal d’événements", [".dmp"] = "Image mémoire",
        [".tmp"] = "Fichier temporaire", [".bak"] = "Sauvegarde", [".old"] = "Ancienne version", [".pf"] = "Préchargement",
        [".jpg"] = "Image JPEG", [".jpeg"] = "Image JPEG", [".png"] = "Image PNG", [".gif"] = "Image GIF", [".bmp"] = "Image bitmap",
        [".webp"] = "Image WebP", [".heic"] = "Image HEIC", [".svg"] = "Image vectorielle", [".ico"] = "Icône", [".raw"] = "Photo brute",
        [".mp4"] = "Vidéo MP4", [".mkv"] = "Vidéo Matroska", [".avi"] = "Vidéo AVI", [".mov"] = "Vidéo QuickTime", [".wmv"] = "Vidéo Windows",
        [".webm"] = "Vidéo WebM", [".mp3"] = "Audio MP3", [".wav"] = "Audio WAV", [".flac"] = "Audio FLAC", [".m4a"] = "Audio AAC",
        [".ogg"] = "Audio Ogg", [".pdf"] = "Document PDF", [".docx"] = "Document Word", [".doc"] = "Document Word", [".xlsx"] = "Classeur Excel",
        [".xls"] = "Classeur Excel", [".pptx"] = "Présentation", [".txt"] = "Texte", [".md"] = "Texte Markdown", [".csv"] = "Données CSV",
        [".json"] = "Données JSON", [".xml"] = "Données XML", [".zip"] = "Archive ZIP", [".7z"] = "Archive 7-Zip", [".rar"] = "Archive RAR",
        [".iso"] = "Image disque", [".vhdx"] = "Disque virtuel", [".vhd"] = "Disque virtuel", [".vmdk"] = "Disque virtuel", [".wim"] = "Image Windows",
        [".esd"] = "Image Windows", [".db"] = "Base de données", [".sqlite"] = "Base de données", [".edb"] = "Base de données", [".pak"] = "Archive de jeu",
        [".pdb"] = "Symboles de débogage", [".lib"] = "Bibliothèque statique", [".obj"] = "Fichier objet", [".rlib"] = "Bibliothèque Rust",
        [".cs"] = "Code C#", [".rs"] = "Code Rust", [".js"] = "Code JavaScript", [".ts"] = "Code TypeScript", [".py"] = "Code Python",
        [".h"] = "En-tête C/C++", [".cpp"] = "Code C++", [".ttf"] = "Police", [".otf"] = "Police", [".ttc"] = "Police",
        [".pri"] = "Ressources d’application", [".winmd"] = "Métadonnées Windows", [".nls"] = "Données de langue", [".dat"] = "Données",
        [".bin"] = "Données binaires", [".msp"] = "Correctif", [".appx"] = "Paquet d’application", [".msix"] = "Paquet d’application",
        [".lnk"] = "Raccourci", [".ini"] = "Configuration", [".reg"] = "Registre", [".ps1"] = "Script PowerShell", [".bat"] = "Script",
        [StorageTree.NoExtension] = "Sans extension"
    };

    public static int ColorFor(ExtensionStats stats)
    {
        if (stats.Rank < Palette.Length)
        {
            return Palette[stats.Rank];
        }
        // Couleur stable et sobre pour les extensions moins volumineuses.
        var hash = 2166136261u;
        foreach (var character in stats.Extension)
        {
            hash = (hash ^ character) * 16777619u;
        }
        var hue = (hash % 360) / 360.0;
        return HslToRgb(hue, 0.28, 0.50);
    }

    public static string Describe(string extension) =>
        Descriptions.TryGetValue(extension, out var description) ? description : $"Fichier {extension.TrimStart('.').ToUpperInvariant()}";

    private static int HslToRgb(double hue, double saturation, double lightness)
    {
        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        int Channel(double t)
        {
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            var value = t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            return (int)Math.Round(value * 255);
        }
        return (Channel(hue + 1.0 / 3) << 16) | (Channel(hue) << 8) | Channel(hue - 1.0 / 3);
    }
}

/// <summary>Formats d'affichage français.</summary>
public static class StorageFormat
{
    private static readonly System.Globalization.CultureInfo French = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    private static readonly string[] Units = ["o", "Ko", "Mo", "Go", "To", "Po"];

    public static string Bytes(ulong bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} o";
        }
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00", French) + " " + Units[unit];
    }

    public static string Count(long count) => count.ToString("N0", French);

    public static string Percent(double fraction) =>
        fraction <= 0 ? "0 %" : fraction < 0.001 ? "< 0,1 %" : (fraction * 100).ToString(fraction >= 0.1 ? "0" : "0.0", French) + " %";

    public static string Date(long fileTime)
    {
        if (fileTime <= 0)
        {
            return "—";
        }
        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime().ToString("dd/MM/yyyy HH:mm", French);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "—";
        }
    }

    public static string Duration(TimeSpan duration) =>
        duration.TotalSeconds < 60 ? duration.TotalSeconds.ToString("0.0", French) + " s" : $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s";
}
