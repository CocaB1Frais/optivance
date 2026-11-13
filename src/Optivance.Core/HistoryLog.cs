using System.Text.Json;

namespace Optivance.Core;

public enum HistoryCategory
{
    Storage,
    Cleaning,
    Optimization,
    Monitoring,
    Settings,
    Application
}

/// <summary>Action de l'utilisateur, enregistrée dans l'historique local.</summary>
public sealed record HistoryEntry(
    DateTimeOffset Time,
    HistoryCategory Category,
    string Action,
    string Target,
    string Details,
    bool Success,
    /// <summary>Données permettant d'annuler l'action (ex. valeur précédente), si possible.</summary>
    string? Undo = null);

/// <summary>
/// Historique des actions (%LOCALAPPDATA%\OPTIVANCE\history.jsonl) : une ligne JSON par action,
/// ajoutée sans réécrire le fichier ; les entrées plus anciennes que la durée de conservation
/// sont retirées au chargement. Aucune donnée ne quitte la machine.
/// </summary>
public sealed class HistoryLog
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly object _gate = new();
    private readonly string _path;

    public HistoryLog(string? path = null)
    {
        _path = path ?? Path.Combine(AppSettings.Folder, "history.jsonl");
    }

    public event Action<HistoryEntry>? Added;

    public void Add(HistoryCategory category, string action, string target, string details, bool success = true, string? undo = null)
    {
        var entry = new HistoryEntry(DateTimeOffset.Now, category, action, target, details, success, undo);
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // L'historique ne doit jamais empêcher l'action elle-même.
        }
        Added?.Invoke(entry);
    }

    /// <summary>Entrées récentes, de la plus récente à la plus ancienne ; purge celles trop anciennes.</summary>
    public List<HistoryEntry> Load(int retentionDays)
    {
        var limit = DateTimeOffset.Now.AddDays(-retentionDays);
        var kept = new List<HistoryEntry>();
        var purged = false;
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return kept;
            }
            foreach (var line in File.ReadLines(_path))
            {
                try
                {
                    if (JsonSerializer.Deserialize<HistoryEntry>(line, Options) is { } entry)
                    {
                        if (entry.Time >= limit)
                        {
                            kept.Add(entry);
                        }
                        else
                        {
                            purged = true;
                        }
                    }
                }
                catch (JsonException)
                {
                    purged = true;
                }
            }
            if (purged)
            {
                File.WriteAllLines(_path, kept.Select(entry => JsonSerializer.Serialize(entry, Options)));
            }
        }
        kept.Reverse();
        return kept;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
