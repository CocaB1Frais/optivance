using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Optivance.Desktop;

public partial class MainWindow : Window
{
    private readonly StoragePage _storagePage = new();
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _storagePage.StatusChanged += SetStatus;
        PageHost.Content = _storagePage;
        UpdateNavigation(StorageNavigation);
        RefreshShellMenuButton();

        // OPTIVANCE.exe --scan <chemin> : analyse lancée dès l'ouverture.
        var arguments = Environment.GetCommandLineArgs();
        var scanIndex = Array.IndexOf(arguments, "--scan");
        var target = scanIndex > 0 && scanIndex + 1 < arguments.Length ? arguments[scanIndex + 1] : null;
        Loaded += async (_, _) =>
        {
            // Autorisation administrateur demandée une fois, au lancement, pour toute la session.
            await _storagePage.InitializeSessionAsync();
            if (target is not null)
            {
                _storagePage.ScanAtStartup(target);
            }
        };
        // Fermeture du composant élevé hors du fil d'interface (pas d'interblocage), 3 s au plus ;
        // à défaut, il s'arrête seul à la fermeture du canal.
        Closed += (_, _) => Task.Run(_storagePage.DisposeSessionAsync).Wait(TimeSpan.FromSeconds(3));
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination } button)
        {
            return;
        }

        PageHost.Content = destination switch
        {
            "Storage" => _storagePage,
            "Monitoring" => new MonitoringPage(),
            "Cleaning" => PlaceholderPage.Create("Cleaning", "Le module de nettoyage sera livré après Monitoring.", "Aucune suppression n’est disponible dans ce prototype."),
            "Optimization" => PlaceholderPage.Create("Optimization", "Optimisation Windows", "La lecture des processus/services et les actions réversibles ne sont pas encore implémentées."),
            "History" => PlaceholderPage.Create("History", "Historique des analyses", "Le stockage persistant et les exports ne sont pas implémentés dans V0.1."),
            "Settings" => PlaceholderPage.Create("Settings", "Paramètres", "Les préférences et l’intégration facultative à Explorer ne sont pas implémentées dans V0.1."),
            _ => _storagePage
        };
        UpdateNavigation(button);
    }

    private void UpdateNavigation(Button selected)
    {
        foreach (var button in FindVisualChildren<Button>(this).Where(button => button.Tag is string))
        {
            button.Background = button == selected ? System.Windows.Media.Brushes.DarkSlateGray : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 45, 58));
            button.BorderBrush = button == selected ? (System.Windows.Media.Brush)FindResource("AccentBrush") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 59, 75));
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }
            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void SetStatus(string message) => GlobalStatus.Text = message;

    private void RefreshShellMenuButton() =>
        ShellMenuText.Text = Optivance.Infrastructure.ShellIntegration.IsRegistered() ? "✓  Dans le clic droit du Bureau (retirer)" : "＋  Ajouter au clic droit du Bureau";

    private void ShellMenuButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Optivance.Infrastructure.ShellIntegration.IsRegistered())
            {
                Optivance.Infrastructure.ShellIntegration.Unregister();
                SetStatus("OPTIVANCE retiré du clic droit du Bureau.");
            }
            else
            {
                Optivance.Infrastructure.ShellIntegration.Register();
                SetStatus("OPTIVANCE ajouté au clic droit du Bureau (sous Windows 11 : aussi dans « Afficher plus d’options »).");
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException or InvalidOperationException)
        {
            SetStatus($"Modification du clic droit impossible : {exception.Message}");
        }
        RefreshShellMenuButton();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_storagePage.IsScanning)
        {
            return;
        }

        e.Cancel = true;
        SetStatus("Annulation de l’analyse avant fermeture…");
        await _storagePage.CancelAndWaitAsync();
        _allowClose = true;
        Close();
    }
}
