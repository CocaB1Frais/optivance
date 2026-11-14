using Optivance.Core;
using Optivance.Infrastructure;
using Optivance.Infrastructure.Optimization;

namespace Optivance.Desktop;

/// <summary>
/// Retour à l'état d'avant OPTIVANCE (valeurs d'origine sauvegardées) : utilisé par
/// « Tout restaurer » et par la désinstallation. Les réglages administrateur demandent le
/// composant de session ; sans lui, ils restent comptés comme non restaurés.
/// </summary>
internal static class OptimizationRestore
{
    public static string ServiceCommand(string name, string mode) =>
        $"{{\"v\":3,\"type\":\"tune-service\",\"name\":\"{name}\",\"start\":\"{mode}\"}}";

    public static string TaskCommand(string id, bool enabled) =>
        $"{{\"v\":3,\"type\":\"tune-task\",\"id\":\"{id}\",\"enabled\":\"{(enabled ? "true" : "false")}\"}}";

    public static async Task<string?> TuneAsync(MftSession? session, string command)
    {
        if (session is null)
        {
            return "autorisation administrateur requise.";
        }
        var code = await session.TuneAsync(command, CancellationToken.None);
        return code == 0 ? null : code == -2 ? "commande refusée par le composant administrateur." : $"Windows a refusé la modification (code {code}).";
    }

    /// <summary>Vrai si des réglages sauvegardés demandent les droits administrateur.</summary>
    public static bool NeedsAdmin(OptimizationBackup backup) =>
        backup.Values.Keys.Any(key => key.StartsWith("svc:") || key.StartsWith("task:")
            || (key.StartsWith("startup:") && !key.StartsWith($"startup:{StartupLocation.UserRun}|") && !key.StartsWith($"startup:{StartupLocation.UserFolder}|")));

    /// <summary>Restaure tout ce qui peut l'être ; renvoie le nombre de réglages non restaurés.</summary>
    public static async Task<int> RestoreAllAsync(OptimizationBackup backup, MftSession? session)
    {
        var failures = 0;
        foreach (var tweak in UserTweaks.All)
        {
            UserTweaks.Restore(tweak, backup);
        }
        if (backup.TryGet("power", out var plan) && Guid.TryParse(plan, out var id))
        {
            if (PowerPlans.SetActive(id)) { backup.Forget("power"); } else { failures++; }
        }
        var startup = StartupApps.List();
        foreach (var (key, value) in backup.Values.ToList())
        {
            string? error = null;
            if (key.StartsWith("svc:"))
            {
                error = await TuneAsync(session, ServiceCommand(key[4..], value is "disabled" or "unknown" ? "demand" : value));
            }
            else if (key.StartsWith("task:"))
            {
                error = await TuneAsync(session, TaskCommand(key[5..], value == "true"));
            }
            else if (key.StartsWith("startup:"))
            {
                if (startup.FirstOrDefault(a => key == $"startup:{a.Location}|{a.Name}") is { } app)
                {
                    var enabled = value == "true";
                    if (app.RequiresAdmin) { error = await TuneAsync(session, StartupApps.MachineCommand(app, enabled)); }
                    else { StartupApps.SetUserEnabled(app, enabled); }
                }
            }
            else
            {
                continue;
            }
            if (error is null) { backup.Forget(key); } else { failures++; }
        }
        return failures;
    }
}
