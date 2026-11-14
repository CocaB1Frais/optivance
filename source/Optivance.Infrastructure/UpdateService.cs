using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Optivance.Core;

namespace Optivance.Infrastructure;

/// <summary>
/// Mise à jour intégrée (exception réseau décidée par le propriétaire le 14/11/2026) :
/// une requête à l'API GitHub des versions, puis — seulement si l'utilisateur accepte —
/// téléchargement de OPTIVANCE.exe, vérification SHA-256 contre l'empreinte publiée,
/// remplacement de l'exécutable (renommé pendant qu'il tourne) et relance.
/// Aucune autre donnée n'est envoyée.
/// </summary>
public static class UpdateService
{
    public const string ExeAsset = "OPTIVANCE.exe";
    public const string ShaAsset = "OPTIVANCE.exe.sha256";
    public const string OldSuffix = ".ancien";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub exige un User-Agent ; nom générique, sans aucune information personnelle.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OPTIVANCE-updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return UpdateRules.ParseTag(text) ?? new Version(0, 0, 0);
        }
    }

    /// <summary>« owner/repo » déduit de l'adresse des versions (Directory.Build.props), null en développement.</summary>
    public static string? Repository
    {
        get
        {
            var url = typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ReleasesUrl")?.Value;
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "github.com")
            {
                return null;
            }
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
        }
    }

    /// <summary>Dernière version publiée si elle est plus récente que celle en cours ; sinon null.</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellation = default, Version? current = null)
    {
        if (Repository is not { } repository)
        {
            return null;
        }
        using var response = await Http.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", cancellation);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation));
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        if (UpdateRules.ParseTag(tag) is not { } version || !UpdateRules.IsNewer(current ?? CurrentVersion, version))
        {
            return null;
        }
        string? exe = null, sha = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var link = asset.GetProperty("browser_download_url").GetString();
            if (name == ExeAsset) { exe = link; size = asset.GetProperty("size").GetInt64(); }
            else if (name == ShaAsset) { sha = link; }
        }
        if (exe is null || !exe.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new ReleaseInfo(version, tag!, notes, exe, sha, size);
    }

    /// <summary>Télécharge et vérifie la nouvelle version ; renvoie le chemin du fichier prêt à installer.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellation = default)
    {
        if (release.ShaUrl is null)
        {
            throw new InvalidDataException("Empreinte de la nouvelle version absente : installation annulée par sécurité.");
        }
        var expected = UpdateRules.ParseSha256(await Http.GetStringAsync(release.ShaUrl, cancellation))
            ?? throw new InvalidDataException("Empreinte de la nouvelle version illisible : installation annulée.");
        var folder = Path.Combine(Path.GetTempPath(), "OPTIVANCE-maj");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"OPTIVANCE-{release.Version}.exe");
        using (var response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, cancellation))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.ExeSize;
            await using var source = await response.Content.ReadAsStreamAsync(cancellation);
            await using var file = File.Create(target);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
                done += read;
                if (total > 0) { progress?.Report((double)done / total); }
            }
        }
        await using (var check = File.OpenRead(target))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellation)).ToLowerInvariant();
            if (actual != expected)
            {
                File.Delete(target);
                throw new InvalidDataException("Le fichier téléchargé ne correspond pas à l’empreinte publiée : installation annulée.");
            }
        }
        return target;
    }

    /// <summary>
    /// Remplace <paramref name="currentExe"/> par <paramref name="downloaded"/> : l'exécutable en cours
    /// est renommé (Windows l'autorise), le nouveau prend sa place. En cas d'échec, l'ancien est remis.
    /// </summary>
    public static void Install(string downloaded, string currentExe)
    {
        var old = currentExe + OldSuffix;
        if (File.Exists(old))
        {
            File.Delete(old);
        }
        File.Move(currentExe, old);
        try
        {
            File.Move(downloaded, currentExe);
        }
        catch
        {
            File.Move(old, currentExe);
            throw;
        }
    }

    /// <summary>Relance la nouvelle version (elle attend la fin de celle-ci avant de démarrer).</summary>
    public static void Restart(string exe) =>
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();

    /// <summary>Au démarrage : supprime l'ancien exécutable laissé par une mise à jour.</summary>
    public static void CleanupOld(string currentExe)
    {
        var old = currentExe + OldSuffix;
        for (var attempt = 0; attempt < 10 && File.Exists(old); attempt++)
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }
}
