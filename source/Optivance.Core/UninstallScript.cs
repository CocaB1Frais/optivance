using System.Text;

namespace Optivance.Core;

/// <summary>
/// Script PowerShell de suppression lancé à la désinstallation, après la fermeture d'OPTIVANCE.
/// Ne supprime que des fichiers nommés : l'exécutable, les deux exécutables du moteur, le
/// raccourci de désinstallation et le dossier de données ; les dossiers ne sont retirés que vides.
/// </summary>
public static class UninstallScript
{
    public const string ShortcutName = "Desinstaller OPTIVANCE.cmd";

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    public static string Build(int processId, string exe, string dataFolder)
    {
        var folder = Path.GetDirectoryName(exe)!;
        var engine = Path.Combine(folder, "moteur");
        return new StringBuilder()
            .AppendLine($"Wait-Process -Id {processId} -ErrorAction SilentlyContinue")
            .AppendLine("Start-Sleep -Seconds 1")
            .AppendLine($"Remove-Item -LiteralPath {Quote(exe)} -Force -ErrorAction SilentlyContinue")
            .AppendLine($"Remove-Item -LiteralPath {Quote(Path.Combine(engine, "optivance-engine.exe"))}, {Quote(Path.Combine(engine, "optivance-mft-helper.exe"))} -Force -ErrorAction SilentlyContinue")
            .AppendLine($"Remove-Item -LiteralPath {Quote(engine)} -ErrorAction SilentlyContinue")
            .AppendLine($"Remove-Item -LiteralPath {Quote(Path.Combine(folder, ShortcutName))} -Force -ErrorAction SilentlyContinue")
            .AppendLine($"Remove-Item -LiteralPath {Quote(dataFolder)} -Recurse -Force -ErrorAction SilentlyContinue")
            .AppendLine($"Remove-Item -LiteralPath {Quote(folder)} -ErrorAction SilentlyContinue")
            .ToString();
    }

    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
