namespace Optivance.Core;

/// <summary>Version publiée sur GitHub et ses fichiers.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string ExeUrl, string? ShaUrl, long ExeSize);

public static class UpdateRules
{
    /// <summary>« v1.2.0 », « 1.2 », « v1.2.0-beta » → 1.2.0 ; null si illisible.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }
        var text = tag.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(text, out var version))
        {
            return null;
        }
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    public static bool IsNewer(Version current, Version candidate) => Normalize(candidate) > Normalize(current);

    /// <summary>Vérification automatique : au plus une fois toutes les 24 h.</summary>
    public static bool CheckDue(DateTime? lastCheckUtc, DateTime nowUtc) => lastCheckUtc is not { } last || nowUtc - last >= TimeSpan.FromHours(24) || last > nowUtc;

    /// <summary>Empreinte attendue dans un fichier « sha256  nom » (format sha256sum).</summary>
    public static string? ParseSha256(string? content)
    {
        var token = content?.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }
}
