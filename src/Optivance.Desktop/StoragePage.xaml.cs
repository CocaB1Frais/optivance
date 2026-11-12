using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Optivance.Core;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

public partial class StoragePage : UserControl
{
    private const int MaxVisibleWarnings = 25;
    private readonly ScanEngineClient _engine = new();
    private readonly ObservableCollection<string> _warnings = [];
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _navigationCancellation;
    private Task? _activeScan;
    private StorageIndex? _index;
    private long _warningCount;
    private int _viewGeneration;
    private int _currentDirectoryId;
    private bool _hasScanResult;

    public event Action<string>? StatusChanged;
    public bool IsScanning => _activeScan is { IsCompleted: false };

    public StoragePage()
    {
        InitializeComponent();
        WarningsList.ItemsSource = _warnings;
        ResultsGrid.LoadingRow += ResultsGrid_LoadingRow;
        DriveSelector.ItemsSource = GetReadyDrives();
        Treemap.TileClicked += SelectRow;
        Treemap.DirectoryDoubleClicked += row => _ = NavigateToAsync(row.Id);
        Treemap.LayoutCompleted += milliseconds =>
        {
            if (milliseconds > 1000)
            {
                SetScanStatus($"Treemap recalculée en {milliseconds:N0} ms.", error: false);
            }
        };
        Treemap.LayoutFailed += exception => SetScanStatus($"La treemap n’a pas pu être calculée : {exception.Message}", error: true);

        if (DriveSelector.Items.Count > 0)
        {
            DriveSelector.SelectedIndex = 0;
        }
        RebuildBreadcrumb(null);
    }

    private static List<DriveChoice> GetReadyDrives()
    {
        var drives = new List<DriveChoice>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Volume" : drive.VolumeLabel;
                    drives.Add(new DriveChoice($"{drive.Name}  ·  {label}", drive.RootDirectory.FullName));
                }
            }
            catch (IOException)
            {
                // Le volume peut être retiré pendant l’inventaire initial.
            }
            catch (UnauthorizedAccessException)
            {
                // Les volumes non accessibles ne sont pas proposés.
            }
        }
        return drives;
    }

    private void DriveSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveSelector.SelectedItem is DriveChoice drive && !IsScanning)
        {
            TargetPath.Text = drive.Path;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Sélectionner un dossier à analyser",
            Multiselect = false,
            InitialDirectory = Directory.Exists(TargetPath.Text)
                ? TargetPath.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            TargetPath.Text = dialog.FolderName;
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        var target = TargetPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            SetScanStatus("Choisissez un dossier ou un volume accessible.", error: true);
            return;
        }
        if (IsScanning)
        {
            return;
        }

        _index = null;
        _hasScanResult = false;
        _warningCount = 0;
        _warnings.Clear();
        ResultsGrid.ItemsSource = null;
        Treemap.SetItems([]);
        TreemapEmptyTitle.Text = "Analyse en cours";
        TreemapEmptyDescription.Text = "Le moteur parcourt la cible et collecte les tailles réelles.";
        TreemapEmptyState.Visibility = Visibility.Visible;
        FilesSummary.Text = LogicalSummary.Text = AllocatedSummary.Text = "—";
        ResultCount.Text = "Analyse en cours";
        WarningsExpander.Header = "Avertissements d’analyse · 0";
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsIndeterminate = true;
        CancelButton.IsEnabled = true;
        AnalyzeButton.IsEnabled = BrowseButton.IsEnabled = DriveSelector.IsEnabled = TargetPath.IsEnabled = false;
        BackButton.IsEnabled = false;
        _scanCancellation = new CancellationTokenSource();
        _activeScan = RunScanAsync(Path.GetFullPath(target), _scanCancellation.Token);
        await _activeScan;
        _activeScan = null;
    }

    private async Task RunScanAsync(string target, CancellationToken cancellationToken)
    {
        try
        {
            SetScanStatus($"Analyse de {target}…", error: false);
            var progress = new Progress<ScanUpdate>(ApplyUpdate);
            var result = await _engine.ScanAsync(target, progress, cancellationToken);
            var summary = result.Summary;
            _index = new StorageIndex(target, result.Entries, summary.State == ScanState.Completed);
            _hasScanResult = true;
            FilesSummary.Text = summary.Files.ToString("N0", CultureInfo.CurrentCulture);
            LogicalSummary.Text = FormatBytes(summary.UniqueLogicalBytes);
            AllocatedSummary.Text = FormatBytes(summary.UniqueAllocatedBytes);
            WarningsExpander.Header = $"Avertissements d’analyse · {summary.Warnings:N0}{(summary.Warnings > _warnings.Count ? " (premiers affichés)" : string.Empty)}";
            _currentDirectoryId = 0;
            await ShowCurrentDirectoryAsync(CancellationToken.None);

            var stateText = summary.State switch
            {
                ScanState.Completed => "Analyse terminée",
                ScanState.Interrupted => "Analyse interrompue · résultats partiels",
                _ => "Analyse échouée"
            };
            SetScanStatus(
                $"{stateText} · {summary.Elapsed.TotalSeconds:N1} s · {summary.EngineMetrics.BytesSent / 1024d / 1024d:N1} Mo transférés",
                error: summary.State == ScanState.Failed);
            ResultCount.Text = $"{summary.Directories:N0} dossiers · {summary.Files:N0} fichiers · {summary.Warnings:N0} avertissements · {summary.Elapsed.TotalSeconds:N1} s";
        }
        catch (OperationCanceledException)
        {
            SetScanStatus("L’analyse a été annulée.", error: false);
            ResultCount.Text = "Analyse annulée";
        }
        catch (Exception exception)
        {
            ResultCount.Text = "Résultats indisponibles";
            TreemapEmptyTitle.Text = "Analyse indisponible";
            TreemapEmptyDescription.Text = "Le moteur n’a pas pu terminer l’analyse.";
            SetScanStatus($"Le moteur n’a pas pu terminer l’analyse : {exception.Message}", error: true);
        }
        finally
        {
            ScanProgress.Visibility = Visibility.Collapsed;
            ScanProgress.IsIndeterminate = false;
            ScanProgress.Value = 0;
            CancelButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = BrowseButton.IsEnabled = DriveSelector.IsEnabled = TargetPath.IsEnabled = true;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void ApplyUpdate(ScanUpdate update)
    {
        switch (update)
        {
            case ScanProgress progress:
                SetScanStatus(
                    $"Analyse en cours · {progress.VisitedDirectories:N0} dossiers · {progress.VisitedFiles:N0} fichiers · {FormatBytes(progress.UniqueLogicalBytes)} uniques · {progress.Elapsed.TotalSeconds:N0} s",
                    error: false);
                break;
            case ScanWarning warning:
                _warningCount++;
                if (_warnings.Count < MaxVisibleWarnings)
                {
                    _warnings.Add($"{warning.Path} — {warning.Message}");
                }
                break;
        }
    }

    private async Task ShowCurrentDirectoryAsync(CancellationToken cancellationToken)
    {
        var index = _index;
        if (index is null)
        {
            return;
        }

        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _navigationCancellation.Token;
        var generation = Interlocked.Increment(ref _viewGeneration);
        var directoryId = _currentDirectoryId;
        var rows = await Task.Run(() => index.BuildRows(directoryId, token), token);
        if (token.IsCancellationRequested || generation != Volatile.Read(ref _viewGeneration))
        {
            return;
        }

        ResultsGrid.ItemsSource = rows;
        ResultsGrid.SelectedItem = null;
        Treemap.SetSelectedId(null);
        Treemap.SetItems(rows);
        var currentEntry = index.GetEntry(directoryId);
        BackButton.IsEnabled = currentEntry.ParentId >= 0;
        DirectoryPath.Text = index.GetPath(directoryId);
        RebuildBreadcrumb(index);
        var positiveCount = rows.Count(row => row.TreemapWeight > 0);
        if (positiveCount > 0)
        {
            TreemapEmptyState.Visibility = Visibility.Collapsed;
        }
        else
        {
            TreemapEmptyTitle.Text = "Aucun élément mesurable";
            TreemapEmptyDescription.Text = "Les éléments vides restent visibles dans le tableau; la treemap utilise les tailles logiques uniques.";
            TreemapEmptyState.Visibility = Visibility.Visible;
        }

        ResultCount.Text = $"{rows.Count:N0} éléments dans ce dossier" + (_hasScanResult ? $" · {index.Count:N0} entrées dans l’index" : string.Empty);
    }

    private async Task NavigateToAsync(int directoryId)
    {
        var index = _index;
        if (index is null || directoryId < 0 || directoryId >= index.Count || !index.GetEntry(directoryId).IsDirectory)
        {
            return;
        }
        _currentDirectoryId = directoryId;
        try
        {
            await ShowCurrentDirectoryAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Une navigation plus récente a remplacé celle-ci.
        }
        catch (Exception exception)
        {
            SetScanStatus($"Impossible d’afficher ce dossier : {exception.Message}", error: true);
        }
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_index is null)
        {
            return;
        }
        var parentId = _index.GetEntry(_currentDirectoryId).ParentId;
        if (parentId >= 0)
        {
            await NavigateToAsync(parentId);
        }
    }

    private void RebuildBreadcrumb(StorageIndex? index)
    {
        BreadcrumbPanel.Children.Clear();
        if (index is null)
        {
            var initial = new TextBlock { Text = "Aucun résultat", Foreground = (Brush)FindResource("MutedTextBrush"), VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
            BreadcrumbPanel.Children.Add(initial);
            return;
        }

        var crumbs = index.BuildBreadcrumb(_currentDirectoryId);
        for (var position = 0; position < crumbs.Count; position++)
        {
            var crumb = crumbs[position];
            if (position > 0)
            {
                BreadcrumbPanel.Children.Add(new TextBlock { Text = "›", Foreground = (Brush)FindResource("MutedTextBrush"), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            var button = new Button
            {
                Content = crumb.Label,
                Tag = crumb.Id,
                Padding = new Thickness(5, 3, 5, 3),
                Margin = new Thickness(0),
                Background = position == crumbs.Count - 1 ? (Brush)FindResource("SelectedSurfaceBrush") : Brushes.Transparent,
                BorderBrush = position == crumbs.Count - 1 ? (Brush)FindResource("AccentStrongBrush") : Brushes.Transparent,
                Foreground = position == crumbs.Count - 1 ? Brushes.White : (Brush)FindResource("MutedTextBrush"),
                FontSize = 10,
                MaxWidth = 360,
                ToolTip = index.GetPath(crumb.Id)
            };
            button.Click += Breadcrumb_Click;
            BreadcrumbPanel.Children.Add(button);
        }
    }

    private async void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int id })
        {
            await NavigateToAsync(id);
        }
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is StorageRow row)
        {
            Treemap.SetSelectedId(row.Id);
        }
        else
        {
            Treemap.SetSelectedId(null);
        }
    }

    private void SelectRow(StorageRow row)
    {
        ResultsGrid.SelectedItem = row;
        ResultsGrid.ScrollIntoView(row);
        Treemap.SetSelectedId(row.Id);
    }

    private async void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not StorageRow row)
        {
            return;
        }
        e.Handled = true;
        await OpenRowAsync(row);
    }

    private void ResultsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = ItemsControl.ContainerFromElement(ResultsGrid, e.OriginalSource as DependencyObject) as DataGridRow;
        if (row is not null)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void ResultsGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => menu.DataContext = menu.PlacementTarget is FrameworkElement target ? target.DataContext : e.Row.DataContext;
        menu.Items.Add(CreateContextItem("Ouvrir", ContextOpen_Click));
        menu.Items.Add(CreateContextItem("Ouvrir l’emplacement dans l’Explorateur", ContextOpenLocation_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateContextItem("Copier le chemin", ContextCopyPath_Click));
        menu.Items.Add(CreateContextItem("Copier le nom", ContextCopyName_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateContextItem("Propriétés", ContextProperties_Click));
        e.Row.ContextMenu = menu;
    }

    private static MenuItem CreateContextItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private StorageRow? ContextRow(object sender) => (sender as FrameworkElement)?.DataContext as StorageRow;

    private async void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow(sender) is { } row)
        {
            await OpenRowAsync(row);
        }
    }

    private void ContextOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow(sender) is { } row)
        {
            try
            {
                if (!PathExists(row))
                {
                    throw new FileNotFoundException("L’élément n’existe plus.", row.Path);
                }
                var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                explorer.ArgumentList.Add($"/select,{row.Path}");
                Process.Start(explorer);
            }
            catch (Exception exception)
            {
                SetScanStatus($"Impossible d’ouvrir l’emplacement : {exception.Message}", error: true);
            }
        }
    }

    private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow(sender) is { } row)
        {
            CopyToClipboard(row.Path);
        }
    }

    private void ContextCopyName_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow(sender) is { } row)
        {
            CopyToClipboard(row.Name);
        }
    }

    private void ContextProperties_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow(sender) is not { } row)
        {
            return;
        }
        try
        {
            if (!PathExists(row))
            {
                throw new FileNotFoundException("L’élément n’existe plus.", row.Path);
            }
            Process.Start(new ProcessStartInfo(row.Path) { Verb = "properties", UseShellExecute = true });
        }
        catch (Exception exception)
        {
            SetScanStatus($"Impossible d’ouvrir les propriétés : {exception.Message}", error: true);
        }
    }

    private async Task OpenRowAsync(StorageRow row)
    {
        if (row.IsDirectory)
        {
            await NavigateToAsync(row.Id);
            return;
        }

        try
        {
            if (!File.Exists(row.Path))
            {
                throw new FileNotFoundException("Le fichier a été déplacé ou supprimé depuis l’analyse.", row.Path);
            }
            Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true });
            SetScanStatus($"Ouverture de {row.Name} avec l’application Windows par défaut.", error: false);
        }
        catch (Exception exception)
        {
            SetScanStatus($"Impossible d’ouvrir le fichier : {exception.Message}", error: true);
        }
    }

    private static bool PathExists(StorageRow row) => row.IsDirectory ? Directory.Exists(row.Path) : File.Exists(row.Path);

    private void CopyToClipboard(string value)
    {
        try
        {
            Clipboard.SetText(value);
            SetScanStatus("Copié dans le presse-papiers.", error: false);
        }
        catch (Exception exception)
        {
            SetScanStatus($"Impossible d’accéder au presse-papiers : {exception.Message}", error: true);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
        CancelButton.IsEnabled = false;
        SetScanStatus("Annulation demandée…", error: false);
    }

    public async Task CancelAndWaitAsync()
    {
        if (_activeScan is not { IsCompleted: false } active)
        {
            return;
        }
        _scanCancellation?.Cancel();
        await active;
    }

    private void SetScanStatus(string message, bool error)
    {
        ScanStatus.Text = message;
        ScanStatus.Foreground = error ? Brushes.OrangeRed : Brushes.Gainsboro;
        StatusChanged?.Invoke(message);
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To", "Po"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:N1} {units[unit]}";
    }

    private sealed record DriveChoice(string Label, string Path)
    {
        public override string ToString() => Label;
    }
}
