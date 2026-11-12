using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Optivance.Infrastructure;

/// <summary>Actions Explorer de l'interface Storage (processus non élevé).</summary>
public static class ShellOperations
{
    /// <summary>
    /// Supprime un fichier ou un dossier : vers la Corbeille par défaut, définitivement si
    /// demandé. La confirmation est faite par l'interface avant l'appel. Renvoie l'erreur
    /// éventuelle (rien n'est considéré supprimé si l'élément existe encore).
    /// </summary>
    public static string? Delete(string path, bool toRecycleBin)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.GetPathRoot(path) == path)
        {
            return "Suppression d’une racine de volume refusée.";
        }
        var operation = new ShFileOperation
        {
            Function = FoDelete,
            From = path + "\0\0",
            Flags = (ushort)(FofNoConfirmation | FofNoErrorUi | FofNoConfirmMkdir | (toRecycleBin ? FofAllowUndo : 0))
        };
        var code = SHFileOperationW(ref operation);
        if (code != 0)
        {
            return $"Windows a refusé la suppression (code 0x{code:X}). Le fichier est peut-être ouvert ou protégé.";
        }
        if (operation.AnyOperationsAborted)
        {
            return "Suppression annulée.";
        }
        return File.Exists(path) || Directory.Exists(path) ? "L’élément existe toujours après la suppression." : null;
    }

    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();

    public static void ShowProperties(string path)
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = SeeMaskInvokeIdList,
            Verb = "properties",
            File = path,
            Show = 1
        };
        ShellExecuteExW(ref info);
    }

    private const uint FoDelete = 0x0003;
    private const int FofNoConfirmation = 0x0010;
    private const int FofAllowUndo = 0x0040;
    private const int FofNoConfirmMkdir = 0x0200;
    private const int FofNoErrorUi = 0x0400;
    private const uint SeeMaskInvokeIdList = 0x0000000C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        public string? Verb;
        public string File;
        public string? Parameters;
        public string? Directory;
        public int Show;
        public IntPtr InstanceApp;
        public IntPtr IdList;
        public string? Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr IconOrMonitor;
        public IntPtr Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOperation operation);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
}
