using Microsoft.Win32;

namespace Optivance.Infrastructure;

/// <summary>
/// Entrée « OPTIVANCE » du clic droit sur le fond du Bureau (et des dossiers), à la demande de
/// l'utilisateur uniquement (bouton dans l'application). Écrite dans le registre de l'utilisateur
/// courant (HKCU), sans droits administrateur, et entièrement retirée par <see cref="Unregister"/>.
/// </summary>
public static class ShellIntegration
{
    private const string KeyPath = @"Software\Classes\Directory\Background\shell\Optivance";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath + @"\command");
        return key?.GetValue(null) is string command && command.Length > 0;
    }

    /// <summary>Inscrit l'entrée vers l'exécutable en cours (à refaire si l'application est déplacée).</summary>
    public static void Register()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l’application inconnu.");
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            key.SetValue(null, "OPTIVANCE");
            key.SetValue("Icon", $"\"{executable}\",0");
        }
        using (var command = Registry.CurrentUser.CreateSubKey(KeyPath + @"\command"))
        {
            command.SetValue(null, $"\"{executable}\"");
        }
    }

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "OPTIVANCE";

    /// <summary>Lancement avec Windows (désactivé par défaut) : valeur du compte courant (HKCU\…\Run).</summary>
    public static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l’application inconnu.");
            key.SetValue(RunValueName, $"\"{executable}\" --tray");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
