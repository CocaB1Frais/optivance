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
        _hotkeys = new Monitoring.GlobalHotkeys(this);
        RegisterHotkeys();
        SettingsPage.SettingsChanged += () =>
        {
            RegisterHotkeys();
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
                "Optimization" => PlaceholderPage.Create("Optimisation", "Optimisation de Windows", "Démarrage, services, alimentation et réglages, avec avertissements et restauration : en cours de développement."),
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
        _widget?.Close();
        Infrastructure.Monitoring.SystemMonitor.Shared.Dispose();
        // Fermeture du composant élevé hors du fil d'interface (pas d'interblocage), 3 s au plus.
        Task.Run(_storagePage.DisposeSessionAsync).Wait(TimeSpan.FromSeconds(3));
        App.History.Add(HistoryCategory.Application, "Fermeture", "OPTIVANCE", "Application quittée");
        Application.Current.Shutdown();
    }
}
