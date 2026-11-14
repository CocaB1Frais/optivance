using System.Reflection;
using System.Security.Cryptography;
using Optivance.Core;

namespace Optivance.Infrastructure;

/// <summary>
/// Exécutables du moteur intégrés à OPTIVANCE.exe (un seul fichier à distribuer). Ils sont
/// extraits dans %LOCALAPPDATA%\OPTIVANCE\moteur\&lt;empreinte&gt;\ et leur empreinte SHA-256 est
/// revérifiée juste avant chaque lancement (réécrits s'ils ont été modifiés). Les anciennes
/// versions extraites sont retirées.
/// </summary>
public static class EngineFiles
{
    public const string EngineName = "optivance-engine.exe";
    public const string HelperName = "optivance-mft-helper.exe";

    private static readonly object Gate = new();
    private static readonly Assembly Self = typeof(EngineFiles).Assembly;
    private static string? _folder;
    private static readonly Dictionary<string, byte[]> Hashes = [];

    private static Stream? Resource(string name) => Self.GetManifestResourceStream($"engine/{name}");

    /// <summary>Dossier des exécutables extraits (ou dossier « moteur » à côté de l'application à défaut).</summary>
    public static string Folder
    {
        get
        {
            lock (Gate)
            {
                return _folder ??= Extract();
            }
        }
    }

    /// <summary>Chemin vérifié d'un exécutable du moteur, à appeler juste avant de le lancer.</summary>
    public static string Prepare(string name)
    {
        var path = Path.Combine(Folder, name);
        lock (Gate)
        {
            if (Hashes.TryGetValue(name, out var expected) && !Matches(path, expected))
            {
                Write(name, path);
            }
        }
        return path;
    }

    private static string Extract()
    {
        using var helper = Resource(HelperName);
        if (helper is null)
        {
            return Path.Combine(AppContext.BaseDirectory, "moteur");
        }
        var version = Convert.ToHexString(SHA256.HashData(helper))[..16].ToLowerInvariant();
        var root = Path.Combine(AppSettings.Folder, "moteur");
        var folder = Path.Combine(root, version);
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { EngineName, HelperName })
        {
            using var stream = Resource(name)!;
            Hashes[name] = SHA256.HashData(stream);
            var path = Path.Combine(folder, name);
            if (!Matches(path, Hashes[name]))
            {
                Write(name, path);
            }
        }
        foreach (var old in Directory.EnumerateDirectories(root).Where(d => !string.Equals(d, folder, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Version précédente encore utilisée (autre instance) : retirée plus tard.
            }
        }
        return folder;
    }

    private static bool Matches(string path, byte[] expected)
    {
        try
        {
            using var file = File.OpenRead(path);
            return SHA256.HashData(file).AsSpan().SequenceEqual(expected);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Write(string name, string path)
    {
        var temporary = path + ".tmp";
        using (var source = Resource(name)!)
        using (var target = File.Create(temporary))
        {
            source.CopyTo(target);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
