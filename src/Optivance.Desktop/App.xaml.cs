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
    private const string InstanceName = @"Local\OPTIVANCE-instance";
    private const string ShowSignalName = @"Local\OPTIVANCE-show";
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;

    public static AppSettings Settings { get; private set; } = new();
    public static HistoryLog History { get; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Une seule instance : relancer OPTIVANCE réaffiche la fenêtre existante.
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var first);
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
