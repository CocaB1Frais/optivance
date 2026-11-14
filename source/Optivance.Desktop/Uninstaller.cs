using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Optivance.Core;
using Optivance.Desktop.Storage;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

/// <summary>
/// Désinstallation d'OPTIVANCE (Paramètres › Désinstaller, ou « Desinstaller OPTIVANCE.cmd »
/// dans le dossier de l'application) :
/// 1. restaure les réglages Windows modifiés par OPTIVANCE (valeurs d'origine) ;
/// 2. retire le lancement avec Windows et l'entrée du clic droit du Bureau ;
/// 3. après la fermeture, supprime OPTIVANCE.exe, le dossier moteur, ce raccourci de
///    désinstallation et les données locales (%LOCALAPPDATA%\OPTIVANCE). Le dossier de
///    l'application n'est retiré que s'il est vide : aucun autre fichier n'est jamais supprimé.
/// </summary>
internal static class Uninstaller
{
    public const string ShortcutName = UninstallScript.ShortcutName;

    /// <summary>Renvoie vrai si la désinstallation est lancée (l'application doit alors se fermer).</summary>
    public static async Task<bool> RunAsync(Window? owner, MftSession? session)
    {
        var exe = Environment.ProcessPath!;
        var folder = Path.GetDirectoryName(exe)!;
        var backup = new OptimizationBackup();
        var details = new List<(string, string)>
        {
            ("Application", exe),
            ("Données", $"{AppSettings.Folder} (paramètres, historique, journaux)"),
        };
        if (backup.Values.Count > 0)
        {
            details.Add(("Réglages Windows", $"{backup.Values.Count} valeur(s) remise(s) à l’état d’avant OPTIVANCE"));
        }
        if (ShellIntegration.IsRegistered() || ShellIntegration.StartsWithWindows())
        {
            details.Add(("Intégration", "lancement avec Windows et clic droit du Bureau retirés"));
        }
        var adminNote = backup.Values.Count > 0 && OptimizationRestore.NeedsAdmin(backup) && session is null
            ? "\n\nCertains réglages (services, tâches planifiées) demandent l’autorisation administrateur pour être restaurés : Windows va la demander."
            : "";
        if (!ConfirmDialog.Ask(owner, "Désinstaller OPTIVANCE ?",
                "OPTIVANCE va restaurer les réglages qu’il a modifiés, puis se fermer et se supprimer de ce PC. Vos fichiers personnels ne sont pas touchés." + adminNote,
                details, "Désinstaller", danger: true))
        {
            return false;
        }

        // 1. Réglages d'origine.
        var failures = 0;
        if (backup.Values.Count > 0)
        {
            MftSession? temporary = null;
            if (session is null && OptimizationRestore.NeedsAdmin(backup) && MftScanClient.IsHelperPresent)
            {
                try
                {
                    temporary = await MftSession.StartAsync(CancellationToken.None);
                }
                catch (ScanEngineException)
                {
                    // Refusé : les réglages administrateur restent tels quels (signalé plus bas).
                }
            }
            failures = await OptimizationRestore.RestoreAllAsync(backup, session ?? temporary);
            if (temporary is not null)
            {
                await temporary.DisposeAsync();
            }
        }

        // 2. Intégration Windows (entrées du compte courant).
        try
        {
            if (ShellIntegration.IsRegistered()) { ShellIntegration.Unregister(); }
            if (ShellIntegration.StartsWithWindows()) { ShellIntegration.SetStartWithWindows(false); }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            failures++;
        }

        // 3. Suppression après la fermeture d'OPTIVANCE (un exécutable ne peut pas se supprimer lui-même).
        ScheduleDeletion(Environment.ProcessId, exe, folder);
        ConfirmDialog.Inform(owner, "OPTIVANCE va être supprimé",
            "OPTIVANCE se ferme maintenant et sera supprimé dans quelques secondes." +
            (failures > 0 ? $"\n\n{failures} réglage(s) n’ont pas pu être restaurés (autorisation administrateur refusée ?). Vous pouvez les rétablir depuis les outils de Windows." : "\n\nTous les réglages modifiés ont été restaurés.") +
            "\n\nMerci d’avoir utilisé OPTIVANCE !");
        return true;
    }

    private static void ScheduleDeletion(int processId, string exe, string folder)
    {
        var encoded = UninstallScript.Encode(UninstallScript.Build(processId, exe, AppSettings.Folder));
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        })?.Dispose();
    }
}