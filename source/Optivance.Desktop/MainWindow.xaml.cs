using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Optivance.Core;
using Optivance.Infrastructure;
using Forms = System.Windows.Forms;

namespace Optivance.Desktop;

public partial class MainWindow : Window
{
    private readonly StoragePage _storagePage = new();
    private readonly Dictionary<string, UIElement> _pages = [];
    private readonly Forms.NotifyIcon _tray;
    private bool _exiting;
    private bool _trayHintShown;

    public MainWindow(MftSession? session, string? sessionError, string? scanTarget, string? startPage = null)
    {
        InitializeComponent();
        Window_StateChanged(this, EventArgs.Empty);
        var version = typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0];
        VersionText.Text = $"OPTIVANCE {version}";
        _storagePage.StatusChanged += SetStatus;
        _storagePage.AttachSession(session, sessionError);
        _storagePage.SessionChanged += UpdateElevationStatus;
        _pages["Storage"] = _storagePage;
        PageHost.Content = _storagePage;
        Select(StorageNavigation);
        UpdateElevationStatus();
        if (sessionError is not null)
        {
            SetStatus(sessionError);
        }

        _tray = CreateTray();
        _alerts = new Monitoring.AlertService((title, message) => _tray.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Warning));
        _fps = new Monitoring.FpsProvider(() => _storagePage.Session);
        Infrastructure.Monitoring.SystemMonitor.Shared.FpsSource = _fps.Read;
        _hotkeys = new Monitoring.GlobalHotkeys(this);
        RegisterHotkeys();
        SettingsPage.SettingsChanged += () =>
        {
            RegisterHotkeys();
            _alerts.ApplySettings();
            _widget?.ApplySettings();
        };
        if (App.Settings.WidgetVisible)
        {
            ShowWidget(true);
        }
        if (startPage is not null && Navigation.Children.OfType<Button>().FirstOrDefault(b => b.Uid == startPage) is { } startButton)
        {
            Loaded += (_, _) => Navigation_Click(startButton, new RoutedEventArgs());
        }
        if (scanTarget is not null)
        {
            Loaded += (_, _) => _storagePage.ScanAtStartup(scanTarget);
        }
    }

    private readonly Monitoring.GlobalHotkeys _hotkeys;
    private readonly Monitoring.AlertService _alerts;
    private readonly Monitoring.FpsProvider _fps;

    /// <summary>FPS mesurables (autorisation administrateur accordée pour la session).</summary>
    public bool FpsAvailable => _fps.Available;

    /// <summary>Ouvre une page (depuis le widget ou la zone de notification).</summary>
    public void OpenPage(string destination)
    {
        ShowFromTray();
        if (Navigation.Children.OfType<Button>().FirstOrDefault(b => b.Uid == destination) is { } button)
        {
            Navigation_Click(button, new RoutedEventArgs());
        }
    }
    private Monitoring.WidgetWindow? _widget;
    private Monitoring.MonitoringPage? _monitoringPage;

    // ───────────────────────────── Widget et raccourcis ─────────────────────────────

    private void RegisterHotkeys()
    {
        var refused = _hotkeys.Register(
        [
            (App.Settings.WidgetToggleHotkey, ToggleWidget),
            (App.Settings.WidgetClickThroughHotkey, ToggleClickThrough)
        ]);
        if (refused.Count > 0)
        {
            SetStatus($"Raccourci indisponible (déjà utilisé par une autre application ?) : {string.Join(", ", refused)}");
        }
    }

    public void ToggleWidget() => ShowWidget(!(App.Settings.WidgetVisible && _widget is { IsVisible: true }));

    private void ShowWidget(bool visible)
    {
        if (visible)
        {
            _widget ??= new Monitoring.WidgetWindow();
            _widget.ApplySettings();
            _widget.Show();
        }
        else
        {
            _widget?.Hide();
        }
        App.Settings.WidgetVisible = visible;
        App.Settings.Save();
        _monitoringPage?.RefreshWidgetButton();
    }

    private void ToggleClickThrough()
    {
        App.Settings.WidgetClickThrough = !App.Settings.WidgetClickThrough;
        App.Settings.Save();
        _widget?.ApplySettings();
        SetStatus(App.Settings.WidgetClickThrough ? "Widget : les clics passent à travers." : "Widget : déplaçable à la souris.");
    }

    public StoragePage Storage => _storagePage;

    // ───────────────────────────── Navigation ─────────────────────────────

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Uid: { Length: > 0 } destination } button)
        {
            return;
        }
        if (!_pages.TryGetValue(destination, out var page))
        {
            page = destination switch
            {
                "Settings" => new SettingsPage(this),
                "History" => new HistoryPage(),
                "Monitoring" => _monitoringPage = new Monitoring.MonitoringPage(ToggleWidget),
                "Cleaning" => new CleaningPage(this),
                "Optimization" => new OptimizationPage(this),
                _ => _storagePage
            };
            _pages[destination] = page;
        }
        PageHost.Content = page;
        Select(button);
    }

    private void Select(Button selected)
    {
        foreach (var child in Navigation.Children.OfType<Button>())
        {
            child.Tag = child == selected ? "Selected" : null;
        }
    }

    private void SetStatus(string message) => GlobalStatus.Text = message;

    private void UpdateElevationStatus() =>
        ElevationStatus.Text = _storagePage.HasActiveSession
            ? "Hors ligne · analyse rapide autorisée pour cette session"
            : "Hors ligne · analyse rapide non autorisée (analyse standard)";

    // ───────────────────────────── Barre de titre intégrée ─────────────────────────────

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        // Fenêtre agrandie : Windows déborde de l'écran de l'épaisseur du cadre ; on la compense.
        var frame = SystemParameters.WindowResizeBorderThickness;
        WindowFrame.Margin = maximized
            ? new Thickness(frame.Left + 2, frame.Top + 2, frame.Right + 2, frame.Bottom + 2)
            : new Thickness(0);
        WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        WindowFrame.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x3B, 0x4B));
        MaximizeGlyph.Data = System.Windows.Media.Geometry.Parse(maximized ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
        MaximizeButton.ToolTip = maximized ? "Niveau inférieur" : "Agrandir";
    }

    // ───────────────────────────── Zone de notification ─────────────────────────────

    private Forms.NotifyIcon CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir OPTIVANCE", null, (_, _) => ShowFromTray());
        menu.Items.Add("Afficher / masquer le widget", null, (_, _) => Dispatcher.BeginInvoke(ToggleWidget));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApplication());
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        var tray = new Forms.NotifyIcon { Icon = icon, Text = "OPTIVANCE", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
        };
        return tray;
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Paramètres › Désinstaller : restaure, nettoie puis ferme OPTIVANCE.</summary>
    public async Task UninstallAsync()
    {
        if (await Uninstaller.RunAsync(this, _storagePage.Session))
        {
            ExitApplication();
        }
    }

    public void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && App.Settings.CloseToTray)
        {
            // Fermer = masquer : OPTIVANCE reste dans la zone de notification (widget, raccourcis).
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "OPTIVANCE reste actif", "OPTIVANCE continue dans la zone de notification. Clic droit sur l’icône → Quitter pour l’arrêter.", Forms.ToolTipIcon.Info);
            }
            return;
        }

        if (_storagePage.IsScanning)
        {
            e.Cancel = true;
            SetStatus("Annulation de l’analyse avant fermeture…");
            await _storagePage.CancelAndWaitAsync();
            _exiting = true;
            Close();
            return;
        }

        _tray.Visible = false;
        _tray.Dispose();
        _hotkeys.Dispose();
        _alerts.Dispose();
        _fps.Dispose();
        _widget?.Close();
        Infrastructure.Monitoring.SystemMonitor.Shared.Dispose();
        // Fermeture du composant élevé hors du fil d'interface (pas d'interblocage), 3 s au plus.
        Task.Run(_storagePage.DisposeSessionAsync).Wait(TimeSpan.FromSeconds(3));
        App.History.Add(HistoryCategory.Application, "Fermeture", "OPTIVANCE", "Application quittée");
        Application.Current.Shutdown();
    }
}
