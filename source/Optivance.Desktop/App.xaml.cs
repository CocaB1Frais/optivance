using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Optivance.Core;
using Optivance.Desktop.Storage;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

public partial class App : Application
{
    // Copie de test (OPTIVANCE_DATA_DIR) : instance distincte, pour ne jamais déranger l'application en cours.
    private static readonly string Suffix = Environment.GetEnvironmentVariable("OPTIVANCE_DATA_DIR") is { Length: > 0 } test
        ? "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(test.ToUpperInvariant())))[..12]
        : "";
    private static readonly string InstanceName = @"Local\OPTIVANCE-instance" + Suffix;
    private static readonly string ShowSignalName = @"Local\OPTIVANCE-show" + Suffix;

    /// <summary>Entrée du clic droit du Bureau ajoutée à ce lancement (notification à afficher).</summary>
    public static bool DesktopMenuJustAdded { get; private set; }
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;

    public static AppSettings Settings { get; private set; } = new();
    public static HistoryLog History { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Une seule instance : relancer OPTIVANCE réaffiche la fenêtre existante.
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
        if (e.Args.Contains("--uninstall"))
        {
            // « Desinstaller OPTIVANCE.cmd » : aucune fenêtre principale, aucune demande d'élévation au démarrage.
            Settings = AppSettings.Load();
            if (!first)
            {
                ConfirmDialog.Inform(null, "OPTIVANCE est ouvert", "Quittez d’abord OPTIVANCE (clic droit sur son icône près de l’horloge › Quitter), puis relancez la désinstallation.");
            }
            else
            {
                await Uninstaller.RunAsync(null, null);
            }
            Shutdown();
            return;
        }
        if (!first)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowSignalName).Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // L'instance existante démarre encore.
            }
            Shutdown();
            return;
        }
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);

        Settings = AppSettings.Load();
        PrepareDesktopMenu();
        var arguments = e.Args;
        var scanIndex = Array.IndexOf(arguments, "--scan");
        var scanTarget = scanIndex >= 0 && scanIndex + 1 < arguments.Length ? arguments[scanIndex + 1] : null;
        var startHidden = arguments.Contains("--tray") && Settings.StartHiddenInTray;

        // L'autorisation administrateur (une fois par session) est demandée AVANT toute fenêtre.
        MftSession? session = null;
        string? sessionError = null;
        if (Settings.RequestElevationAtStartup && !arguments.Contains("--no-elevation") && MftScanClient.IsHelperPresent)
        {
            try
            {
                session = await MftSession.StartAsync(CancellationToken.None);
            }
            catch (ScanEngineException exception)
            {
                sessionError = exception.Message;
            }
        }

        var pageIndex = Array.IndexOf(arguments, "--page");
        var window = new MainWindow(session, sessionError, scanTarget, pageIndex >= 0 && pageIndex + 1 < arguments.Length ? arguments[pageIndex + 1] : null);
        MainWindow = window;
        if (!startHidden)
        {
            window.Show();
        }
        History.Add(HistoryCategory.Application, "Démarrage", "OPTIVANCE", session is null ? "Analyse rapide non autorisée" : "Analyse rapide autorisée pour la session");

        var signal = _showSignal;
        _ = Task.Run(() =>
        {
            while (signal.WaitOne())
            {
                Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray());
            }
        });
    }

    /// <summary>
    /// Clic droit du Bureau : ajouté au premier lancement pour que chacun découvre la fonction
    /// (décision du propriétaire du 14/11/2026), annoncé par une notification, désactivable dans
    /// Paramètres ; jamais rajouté ensuite. Mis à jour si l'application a été déplacée. Aucune
    /// écriture pour une copie de test (OPTIVANCE_DATA_DIR).
    /// </summary>
    private static void PrepareDesktopMenu()
    {
        if (Suffix.Length > 0)
        {
            return;
        }
        try
        {
            if (!Settings.DesktopMenuOffered)
            {
                if (!ShellIntegration.IsRegistered())
                {
                    ShellIntegration.Register();
                    DesktopMenuJustAdded = true;
                    History.Add(HistoryCategory.Settings, "Paramètre modifié", "« OPTIVANCE » dans le clic droit du Bureau", "ajouté au premier lancement");
                }
                Settings.DesktopMenuOffered = true;
                Settings.Save();
            }
            else
            {
                ShellIntegration.RefreshIfMoved();
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Registre inaccessible : la fonction reste disponible depuis Paramètres.
        }
    }

    /// <summary>Filet de sécurité : une erreur inattendue est journalisée et signalée, l'application continue.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var file = Log(e.Exception);
        e.Handled = true;
        ConfirmDialog.Inform(MainWindow, "Une erreur inattendue s’est produite",
            $"OPTIVANCE continue de fonctionner, mais l’action en cours a été interrompue.\n\n{e.Exception.Message}" +
            (file is null ? "" : $"\n\nDétails enregistrés dans : {file}"));
    }

    private static string? Log(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }
        try
        {
            var folder = Path.Combine(AppSettings.Folder, "logs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"erreur-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(file, $"{DateTime.Now:O}\n{exception}");
            return file;
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
