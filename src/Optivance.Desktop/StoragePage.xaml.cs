using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Optivance.Core;
using Optivance.Core.Storage;
using Optivance.Desktop.Storage;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

public partial class StoragePage : UserControl
{
    private const int DirectoryColor = 0x5A6272;

    private readonly StorageScanner _scanner = new();
    private readonly RowCollection<TreeRow> _rows = new();
    private readonly HashSet<int> _expanded = [];
    private readonly List<ScanWarning> _warnings = [];
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Stopwatch _scanWatch = new();

    private CancellationTokenSource? _scanCancellation;
    private Task? _scanTask;
    private StorageTree? _tree;
    private ScanSummary? _summary;
    private int[] _extensionColors = [];
    private int[] _allFiles = [];
    private int? _extensionFilter;
    private StorageSortKey _sortKey = StorageSortKey.Size;
    private bool _sortDescending = true;
    private long _progressItems;
    private bool _syncing;
    private int _filterGeneration;
    private TimeSpan _displayTime;

    public StoragePage()
    {
        InitializeComponent();
        TreeList.ItemsSource = _rows;
        var drives = DriveItem.Load();
        DriveSelector.ItemsSource = drives;
        DriveSelector.SelectedItem = drives.FirstOrDefault(drive => drive.Root.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) ?? drives.FirstOrDefault();

        _clock.Tick += (_, _) => UpdateOverlay();
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            _ = ApplyFileFilterAsync();
        };
        Treemap.ItemClicked += Treemap_ItemClicked;
        Treemap.ItemDoubleClicked += Treemap_ItemDoubleClicked;
        Treemap.ItemRightClicked += Treemap_ItemRightClicked;
        Treemap.HoverChanged += Treemap_HoverChanged;
        PreviewKeyDown += Page_PreviewKeyDown;
    }

    public event Action<string>? StatusChanged;

    public bool IsScanning => _scanTask is { IsCompleted: false };

    public async Task CancelAndWaitAsync()
    {
        _scanCancellation?.Cancel();
        if (_scanTask is { } task)
        {
            try
            {
                await task;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException)
            {
                // L'erreur a déjà été présentée ou l'analyse a été annulée.
            }
        }
    }

    // ───────────────────────────── Analyse ─────────────────────────────

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (DriveSelector.SelectedItem is DriveItem drive)
        {
            StartScan(drive.Root, preferMft: true);
        }
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choisir le dossier à analyser" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            StartScan(dialog.FolderName, preferMft: true);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
        OverlayTitle.Text = "Annulation…";
    }

    private void StartScan(string target, bool preferMft)
    {
        if (IsScanning)
        {
            return;
        }
        _scanTask = RunScanAsync(target, preferMft);
    }

    private async Task RunScanAsync(string target, bool preferMft)
    {
        _scanCancellation = new CancellationTokenSource();
        _warnings.Clear();
        _progressItems = 0;
        var fast = preferMft && MftScanClient.CanScan(target, out _) && _scanner.Session is { IsActive: true };
        SetScanning(true, fast
            ? "Lecture directe de la table des fichiers (MFT), en lecture seule."
            : "Analyse standard : parcours des dossiers.");
        OverlayTitle.Text = fast ? "Analyse rapide en cours…" : "Analyse en cours…";
        Status($"Analyse de {target}…");
        var progress = new Progress<ScanUpdate>(update =>
        {
            switch (update)
            {
                case ScanProgress scanProgress:
                    if (_progressItems == 0)
                    {
                        OverlayHint.Text = fast ? "Lecture de la table des fichiers (MFT) en lecture seule…" : "Parcours des dossiers…";
                    }
                    _progressItems = Math.Max(_progressItems, scanProgress.VisitedFiles + scanProgress.VisitedDirectories);
                    break;
                case ScanWarning warning:
                    _warnings.Add(warning);
                    if (warning.Message.StartsWith("Analyse rapide", StringComparison.Ordinal))
                    {
                        OverlayTitle.Text = "Analyse standard en cours…";
                        OverlayHint.Text = warning.Message;
                    }
                    break;
            }
        });

        try
        {
            var result = await _scanner.ScanAsync(target, preferMft, progress, _scanCancellation.Token);
            OverlayTitle.Text = "Préparation de l’affichage…";
            var display = Stopwatch.StartNew();
            var (tree, files) = await Task.Run(() =>
            {
                var built = new StorageTree(Path.GetFullPath(target), result.Entries);
                return (built, built.FilesBySize());
            });
            ShowResult(tree, files, result.Summary);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            display.Stop();
            _displayTime = display.Elapsed;
            Status($"{tree.RootPath} · {StorageFormat.Bytes(tree.TotalBytes)} · {StorageFormat.Count(tree.Files(0))} fichiers · analyse {StorageFormat.Duration(result.Summary.Elapsed)} ({MethodLabel(result.Summary.Method)}) + affichage {StorageFormat.Duration(display.Elapsed)} · total {StorageFormat.Duration(_scanWatch.Elapsed)}");
        }
        catch (OperationCanceledException)
        {
            Status("Analyse annulée.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or InvalidDataException or UnauthorizedAccessException)
        {
            Status("Analyse impossible.");
            ConfirmDialog.Inform(Window.GetWindow(this), "Analyse impossible", exception.Message);
        }
        finally
        {
            SetScanning(false, "");
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    /// <summary>
    /// Une seule demande d'autorisation administrateur par session (choix du propriétaire) :
    /// le composant d'analyse rapide reste actif jusqu'à la fermeture d'OPTIVANCE.
    /// </summary>
    public async Task InitializeSessionAsync()
    {
        if (_scanner.Session is { IsActive: true })
        {
            return;
        }
        try
        {
            Status("Demande d’autorisation pour l’analyse rapide…");
            _scanner.Session = await MftSession.StartAsync(CancellationToken.None);
            EnableFastButton.Visibility = Visibility.Collapsed;
            Status("Analyse rapide (MFT) autorisée pour cette session.");
        }
        catch (ScanEngineException exception)
        {
            EnableFastButton.Visibility = Visibility.Visible;
            Status(exception.Message);
        }
    }

    public async Task DisposeSessionAsync()
    {
        if (_scanner.Session is { } session)
        {
            _scanner.Session = null;
            await session.DisposeAsync();
        }
    }

    private async void EnableFastButton_Click(object sender, RoutedEventArgs e) => await InitializeSessionAsync();

    /// <summary>Lance une analyse au démarrage (option <c>--scan &lt;chemin&gt;</c>).</summary>
    public void ScanAtStartup(string target)
    {
        var full = Path.GetFullPath(target);
        if (Directory.Exists(full))
        {
            StartScan(full, preferMft: true);
        }
        else
        {
            ConfirmDialog.Inform(Window.GetWindow(this), "Analyse impossible", $"Le dossier n’existe pas : {full}");
        }
    }

    private void SetScanning(bool scanning, string hint)
    {
        if (scanning)
        {
            WelcomePanel.Visibility = Visibility.Collapsed;
        }
        else if (_tree is null)
        {
            WelcomePanel.Visibility = Visibility.Visible;
        }
        ScanOverlay.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !scanning;
        FolderButton.IsEnabled = !scanning;
        DriveSelector.IsEnabled = !scanning;
        OverlayHint.Text = hint;
        if (scanning)
        {
            _scanWatch.Restart();
            _clock.Start();
            UpdateOverlay();
        }
        else
        {
            _clock.Stop();
            _scanWatch.Stop();
        }
    }

    private void UpdateOverlay() =>
        OverlayCounters.Text = $"{StorageFormat.Count(_progressItems)} éléments · {StorageFormat.Duration(_scanWatch.Elapsed)}";

    private void ShowResult(StorageTree tree, int[] files, ScanSummary summary)
    {
        _tree = tree;
        _summary = summary;
        _allFiles = files;
        _extensionFilter = null;
        _extensionColors = tree.Extensions.Select(ExtensionCatalog.ColorFor).ToArray();
        _expanded.Clear();
        _expanded.Add(0);
        SearchBox.Text = "";

        WelcomePanel.Visibility = Visibility.Collapsed;
        SummaryCards.Visibility = Visibility.Visible;
        MainArea.Visibility = Visibility.Visible;
        TreemapSplitter.Visibility = Visibility.Visible;
        TreemapCard.Visibility = Visibility.Visible;

        RebuildRows(select: 0);
        RefreshExtensions();
        _ = ApplyFileFilterAsync();
        UpdateSummary();
        ZoomTreemap(0);
        ShowNotice();
        Status($"{tree.RootPath} · {StorageFormat.Bytes(tree.TotalBytes)} · {StorageFormat.Count(tree.Files(0))} fichiers · {StorageFormat.Duration(summary.Elapsed)} ({MethodLabel(summary.Method)})");
    }

    private void UpdateSummary()
    {
        if (_tree is null || _summary is null)
        {
            return;
        }
        var tree = _tree;
        var isDrive = string.Equals(Path.GetPathRoot(tree.RootPath), tree.RootPath, StringComparison.OrdinalIgnoreCase);
        try
        {
            var drive = new DriveInfo(tree.RootPath);
            var used = (ulong)(drive.TotalSize - drive.TotalFreeSpace);
            DiskUsedText.Text = $"{StorageFormat.Bytes(used)} / {StorageFormat.Bytes((ulong)drive.TotalSize)}";
            DiskUsedBar.Width = Math.Max(2, ((Border)DiskUsedBar.Parent).ActualWidth * used / Math.Max(1, drive.TotalSize));
            DiskUsedBar.Tag = (double)used / Math.Max(1, drive.TotalSize);
            DiskDetailText.Text = isDrive
                ? $"{StorageFormat.Bytes((ulong)drive.TotalFreeSpace)} libres · {drive.DriveFormat}"
                : $"Dossier analysé sur {drive.Name.TrimEnd('\\')}";
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            DiskUsedText.Text = "—";
            DiskDetailText.Text = "Volume indisponible";
        }
        TotalText.Text = StorageFormat.Bytes(tree.TotalBytes);
        TotalDetailText.Text = $"{StorageFormat.Bytes(tree.TotalAllocated)} alloués sur le disque";
        FilesText.Text = StorageFormat.Count(tree.Files(0));
        FilesDetailText.Text = $"{StorageFormat.Count(tree.Extensions.Count(e => e.Files > 0))} types de fichiers";
        FoldersText.Text = StorageFormat.Count(tree.Folders(0));
        FoldersDetailText.Text = isDrive ? tree.RootPath : Path.GetFileName(tree.RootPath.TrimEnd('\\'));
        DurationText.Text = StorageFormat.Duration(_summary.Elapsed);
        StateText.Text = $"{MethodLabel(_summary.Method)} · {StateLabel(_summary)}";
        MethodText.Text = _summary.Method == ScanMethods.Classic ? "ANALYSE STANDARD" : "ANALYSE RAPIDE · MFT";
        MethodIcon.Text = _summary.Method == ScanMethods.Classic ? "" : "";
        MethodBadge.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (DiskUsedBar.Tag is double fraction)
            {
                DiskUsedBar.Width = Math.Max(2, ((Border)DiskUsedBar.Parent).ActualWidth * fraction);
            }
        });
    }

    private void ShowNotice()
    {
        if (_summary is null)
        {
            return;
        }
        string? notice = _summary.Notice;
        if (notice is null && _summary.IsPartial)
        {
            var details = string.Join(" ", _warnings.Take(3).Select(warning => warning.Message));
            notice = $"Analyse rapide partielle : certaines entrées n’ont pas pu être lues. {details}";
        }
        else if (notice is null && _summary.State == ScanState.Interrupted)
        {
            notice = "Analyse interrompue : les résultats affichés sont incomplets.";
        }
        else if (notice is null && _summary.Method == ScanMethods.Classic && _summary.Warnings > 0)
        {
            notice = $"{StorageFormat.Count(_summary.Warnings)} élément(s) inaccessible(s) ou ignoré(s) (droits, liens). L’analyse rapide (MFT) lit tout le disque.";
        }
        NoticeText.Text = notice ?? "";
        NoticeBar.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HideNotice_Click(object sender, RoutedEventArgs e) => NoticeBar.Visibility = Visibility.Collapsed;

    private static string MethodLabel(string method) => method switch
    {
        ScanMethods.MftRaw => "MFT directe",
        ScanMethods.MftEnum => "MFT (énumération)",
        _ => "Standard"
    };

    private static string StateLabel(ScanSummary summary) => summary.State switch
    {
        ScanState.Completed => "terminée",
        ScanState.Interrupted when summary.IsPartial => "partielle",
        ScanState.Interrupted => "interrompue",
        _ => "échec"
    };

    // ───────────────────────────── Couleurs ─────────────────────────────

    private int ColorOf(int id)
    {
        if (_tree is null)
        {
            return DirectoryColor;
        }
        var extension = _tree.ExtensionId(id);
        if (extension < 0 || extension >= _extensionColors.Length)
        {
            return DirectoryColor;
        }
        var color = _extensionColors[extension];
        return _extensionFilter is { } filter && filter != extension ? Blend(color, 0x20242C, 0.82) : color;
    }

    private static int Blend(int rgb, int with, double amount)
    {
        int Channel(int shift) => (int)Math.Round(((rgb >> shift) & 0xFF) * (1 - amount) + ((with >> shift) & 0xFF) * amount);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    // ───────────────────────────── Arborescence ─────────────────────────────

    private void RebuildRows(int? select = null)
    {
        if (_tree is null)
        {
            return;
        }
        var selectedId = select ?? (TreeList.SelectedItem as TreeRow)?.Id;
        var rows = new List<TreeRow>(Math.Min(_tree.Count, 4096));
        AppendRows(rows, 0, 0);
        _syncing = true;
        _rows.Reset(rows);
        if (selectedId is { } id)
        {
            var row = rows.FirstOrDefault(candidate => candidate.Id == id);
            TreeList.SelectedItem = row;
            if (row is not null)
            {
                TreeList.ScrollIntoView(row);
            }
        }
        _syncing = false;
    }

    private void AppendRows(List<TreeRow> rows, int id, int depth)
    {
        var tree = _tree!;
        var row = new TreeRow(tree, id, depth, ColorOf);
        rows.Add(row);
        if (row.HasChildren && _expanded.Contains(id))
        {
            row.IsExpanded = true;
            foreach (var child in tree.Children(id, _sortKey, _sortDescending))
            {
                AppendRows(rows, child, depth + 1);
            }
        }
    }

    private void Toggle(TreeRow row, bool? expand = null)
    {
        if (_tree is null || !row.HasChildren)
        {
            return;
        }
        var shouldExpand = expand ?? !row.IsExpanded;
        if (shouldExpand == row.IsExpanded)
        {
            return;
        }
        var items = _rows.Items;
        var index = items.IndexOf(row);
        if (index < 0)
        {
            return;
        }
        if (shouldExpand)
        {
            _expanded.Add(row.Id);
            row.IsExpanded = true;
            var inserted = new List<TreeRow>();
            foreach (var child in _tree.Children(row.Id, _sortKey, _sortDescending))
            {
                AppendRows(inserted, child, row.Depth + 1);
            }
            items.InsertRange(index + 1, inserted);
        }
        else
        {
            _expanded.Remove(row.Id);
            row.IsExpanded = false;
            var end = index + 1;
            while (end < items.Count && items[end].Depth > row.Depth)
            {
                end++;
            }
            items.RemoveRange(index + 1, end - index - 1);
        }
        _syncing = true;
        _rows.Changed();
        TreeList.SelectedItem = row;
        _syncing = false;
    }

    private void Expander_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreeRow row })
        {
            Toggle(row);
            e.Handled = true;
        }
    }

    private void TreeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TreeList.SelectedItem is not TreeRow row || e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(TreeList, source) is null)
        {
            return;
        }
        if (row.IsDirectory)
        {
            Toggle(row);
        }
        else
        {
            Run(() => ShellOperations.Open(_tree!.GetPath(row.Id)));
        }
    }

    private void TreeList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TreeList.SelectedItem is not TreeRow row || _tree is null)
        {
            return;
        }
        switch (e.Key)
        {
            case Key.Right when row.HasChildren && !row.IsExpanded:
                Toggle(row, true);
                e.Handled = true;
                break;
            case Key.Left when row.IsExpanded:
                Toggle(row, false);
                e.Handled = true;
                break;
            case Key.Left when row.Id > 0:
                RevealInTree(_tree.Parent(row.Id));
                e.Handled = true;
                break;
            case Key.Enter:
                if (row.IsDirectory)
                {
                    Toggle(row);
                }
                else
                {
                    Run(() => ShellOperations.Open(_tree.GetPath(row.Id)));
                }
                e.Handled = true;
                break;
            case Key.Delete:
                _ = DeleteAsync(row.Id, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                break;
        }
    }

    private void TreeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || TreeList.SelectedItem is not TreeRow row || _tree is null)
        {
            return;
        }
        Treemap.SetSelected(row.Id);
        Status(_tree.GetPath(row.Id));
    }

    private void TreeHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string tag } || !Enum.TryParse<StorageSortKey>(tag, out var key))
        {
            return;
        }
        _sortDescending = key == _sortKey ? !_sortDescending : key != StorageSortKey.Name;
        _sortKey = key;
        RebuildRows();
    }

    /// <summary>Déplie les ancêtres puis sélectionne l'élément dans l'arborescence.</summary>
    private void RevealInTree(int id)
    {
        if (_tree is null)
        {
            return;
        }
        foreach (var ancestor in _tree.Ancestry(id).SkipLast(1))
        {
            _expanded.Add(ancestor);
        }
        ViewTabs.SelectedIndex = 0;
        RebuildRows(select: id);
        Treemap.SetSelected(id);
        TreeList.Focus();
    }

    // ───────────────────────────── Treemap ─────────────────────────────

    private void ZoomTreemap(int id)
    {
        if (_tree is null)
        {
            return;
        }
        var root = _tree.IsDirectory(id) ? id : Math.Max(0, _tree.Parent(id));
        Treemap.SetData(_tree, root, ColorOf);
        TreemapTitle.Text = $"{_tree.GetPath(root)}  ·  {StorageFormat.Bytes(_tree.Size(root))}";
        TreemapUpButton.IsEnabled = root != 0;
    }

    private void Treemap_ItemClicked(int id) => RevealInTree(id);

    private void Treemap_ItemDoubleClicked(int id) => ZoomTreemap(id);

    private void Treemap_ItemRightClicked(int id)
    {
        RevealInTree(id);
        var menu = BuildMenu(id);
        menu.PlacementTarget = Treemap;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void Treemap_HoverChanged(int id)
    {
        if (_tree is null || id < 0)
        {
            TreemapHover.Text = "";
            return;
        }
        var extension = _tree.ExtensionId(id);
        var kind = extension >= 0 ? ExtensionCatalog.Describe(_tree.Extensions[extension].Extension) : "Dossier";
        TreemapHover.Text = $"{_tree.GetPath(id)}  ·  {StorageFormat.Bytes(_tree.Size(id))}  ·  {kind}";
    }

    private void TreemapUp_Click(object sender, RoutedEventArgs e)
    {
        if (_tree is not null && Treemap.RootId > 0)
        {
            ZoomTreemap(_tree.Parent(Treemap.RootId));
        }
    }

    private void TreemapReset_Click(object sender, RoutedEventArgs e) => ZoomTreemap(0);

    // ───────────────────────────── Extensions et fichiers ─────────────────────────────

    private void RefreshExtensions()
    {
        if (_tree is null)
        {
            return;
        }
        var selected = (ExtensionList.SelectedItem as ExtensionRow)?.Stats.Id;
        var rows = _tree.Extensions.Where(stats => stats.Files > 0)
            .OrderByDescending(stats => stats.Bytes)
            .Select(stats => new ExtensionRow(stats, _tree.TotalBytes, _extensionColors[stats.Id]))
            .ToList();
        _syncing = true;
        ExtensionList.ItemsSource = rows;
        ExtensionList.SelectedItem = rows.FirstOrDefault(row => row.Stats.Id == selected);
        _syncing = false;
        ExtensionCount.Text = $"{StorageFormat.Count(rows.Count)} types";
    }

    private void ExtensionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _tree is null)
        {
            return;
        }
        if (ExtensionList.SelectedItem is ExtensionRow row)
        {
            SetExtensionFilter(row.Stats.Id);
        }
    }

    private void SetExtensionFilter(int? extension)
    {
        if (_tree is null)
        {
            return;
        }
        _extensionFilter = extension;
        if (extension is { } id)
        {
            var stats = _tree.Extensions[id];
            FilterText.Text = $"{stats.Extension} · {ExtensionCatalog.Describe(stats.Extension)} · {StorageFormat.Bytes(stats.Bytes)}";
            FilterSwatch.Background = Palette.Brush(_extensionColors[id]);
            FilterChip.Visibility = Visibility.Visible;
            ViewTabs.SelectedItem = FilesTab;
        }
        else
        {
            FilterChip.Visibility = Visibility.Collapsed;
            _syncing = true;
            ExtensionList.SelectedItem = null;
            _syncing = false;
        }
        Treemap.Refresh(ColorOf);
        _ = ApplyFileFilterAsync();
    }

    private void ClearExtensionFilter_Click(object sender, RoutedEventArgs e) => SetExtensionFilter(null);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var empty = string.IsNullOrEmpty(SearchBox.Text);
        SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (!empty && _tree is not null)
        {
            ViewTabs.SelectedItem = FilesTab;
        }
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Text = "";

    private async Task ApplyFileFilterAsync()
    {
        if (_tree is null)
        {
            return;
        }
        var tree = _tree;
        var source = _allFiles;
        var extension = _extensionFilter;
        var search = SearchBox.Text.Trim();
        var generation = ++_filterGeneration;
        var ids = await Task.Run(() =>
        {
            if (extension is null && search.Length == 0)
            {
                return source;
            }
            var result = new List<int>();
            foreach (var id in source)
            {
                if (extension is { } wanted && tree.ExtensionId(id) != wanted)
                {
                    continue;
                }
                if (search.Length > 0 && tree.Name(id).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                result.Add(id);
            }
            return result.ToArray();
        });
        if (generation != _filterGeneration)
        {
            return;
        }
        FileList.ItemsSource = new VirtualFileList(tree, ids, ColorOf);
        FilesTabCount.Text = StorageFormat.Count(ids.Length);
    }

    private void ViewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Rien de particulier : les deux vues restent synchronisées avec la treemap.
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row && _tree is not null)
        {
            Treemap.SetSelected(row.Id);
            Status(_tree.GetPath(row.Id));
        }
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is FileRow row && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(FileList, source) is not null)
        {
            RevealInTree(row.Id);
        }
    }

    private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (FileList.SelectedItem is not FileRow row || _tree is null)
        {
            return;
        }
        if (e.Key == Key.Delete)
        {
            _ = DeleteAsync(row.Id, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Run(() => ShellOperations.Open(_tree.GetPath(row.Id)));
            e.Handled = true;
        }
    }

    // ───────────────────────────── Menu contextuel ─────────────────────────────

    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var id = sender == TreeList ? (TreeList.SelectedItem as TreeRow)?.Id : (FileList.SelectedItem as FileRow)?.Id;
        if (id is null || _tree is null || sender is not FrameworkElement element)
        {
            e.Handled = true;
            return;
        }
        element.ContextMenu = BuildMenu(id.Value);
    }

    private ContextMenu BuildMenu(int id)
    {
        var tree = _tree!;
        var path = tree.GetPath(id);
        var menu = new ContextMenu();
        menu.Items.Add(Item("", tree.IsDirectory(id) ? "Ouvrir le dossier" : "Ouvrir", "Entrée", () => ShellOperations.Open(path)));
        menu.Items.Add(Item("", "Afficher dans l’Explorateur", null, () => ShellOperations.ShowInExplorer(path)));
        menu.Items.Add(Item("", "Copier le chemin", null, () => Clipboard.SetText(path)));
        menu.Items.Add(Item("", "Copier le nom", null, () => Clipboard.SetText(id == 0 ? path : tree.Name(id))));
        menu.Items.Add(Item("", "Propriétés", null, () => ShellOperations.ShowProperties(path)));
        if (tree.IsDirectory(id))
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("", "Centrer la treemap ici", null, () => ZoomTreemap(id)));
            if (id != 0)
            {
                menu.Items.Add(Item("", "Analyser ce dossier", null, () => StartScan(path, preferMft: false)));
            }
        }
        if (id != 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("", "Mettre à la Corbeille", "Suppr", () => _ = DeleteAsync(id, permanent: false)));
            menu.Items.Add(Item("", "Supprimer définitivement", "Maj+Suppr", () => _ = DeleteAsync(id, permanent: true), danger: true));
        }
        return menu;
    }

    private MenuItem Item(string glyph, string text, string? gesture, Action action, bool danger = false)
    {
        var grid = new Grid { MinWidth = 250 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var foreground = danger ? Palette.Brush(0xF08A80) : null;
        var icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (foreground is not null)
        {
            icon.Foreground = foreground;
            label.Foreground = foreground;
        }
        grid.Children.Add(icon);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (gesture is not null)
        {
            var hint = new TextBlock { Text = gesture, Foreground = Palette.Brush(0x7D8899), FontSize = 11, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }
        var item = new MenuItem { Header = grid };
        item.Click += (_, _) => Run(action);
        return item;
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            ConfirmDialog.Inform(Window.GetWindow(this), "Action impossible", exception.Message);
        }
    }

    // ───────────────────────────── Suppression ─────────────────────────────

    private async Task DeleteAsync(int id, bool permanent)
    {
        if (_tree is null || id <= 0 || IsScanning || _tree.IsDeleted(id))
        {
            return;
        }
        var tree = _tree;
        var path = tree.GetPath(id);
        var isDirectory = tree.IsDirectory(id);
        var details = new List<(string, string)>
        {
            ("Élément", tree.Name(id)),
            ("Emplacement", tree.GetPath(tree.Parent(id))),
            ("Taille", StorageFormat.Bytes(tree.Size(id)))
        };
        if (isDirectory)
        {
            details.Add(("Contenu", $"{StorageFormat.Count(tree.Files(id))} fichiers, {StorageFormat.Count(tree.Folders(id))} dossiers"));
        }
        if (tree.IsDuplicate(id))
        {
            details.Add(("Remarque", "Lien physique supplémentaire : le supprimer ne libère pas d’espace."));
        }
        var confirmed = permanent
            ? ConfirmDialog.Ask(Window.GetWindow(this), "Supprimer définitivement ?", "Cet élément sera effacé sans passer par la Corbeille. Cette action est irréversible.", details, "Supprimer définitivement", danger: true)
            : ConfirmDialog.Ask(Window.GetWindow(this), "Mettre à la Corbeille ?", "L’élément sera déplacé dans la Corbeille ; vous pourrez le restaurer depuis celle-ci.", details, "Mettre à la Corbeille", danger: false);
        if (!confirmed)
        {
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        string? error;
        try
        {
            Status($"Suppression de {path}…");
            error = await Task.Run(() => ShellOperations.Delete(path, toRecycleBin: !permanent));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        if (error is not null)
        {
            Status("Suppression impossible.");
            ConfirmDialog.Inform(Window.GetWindow(this), "Suppression impossible", $"{path}\n\n{error}");
            return;
        }

        var parent = tree.Parent(id);
        var freed = tree.Remove(id);
        _allFiles = await Task.Run(tree.FilesBySize);
        RebuildRows(select: parent);
        RefreshExtensions();
        _ = ApplyFileFilterAsync();
        UpdateSummary();
        Treemap.Refresh(ColorOf);
        Status(permanent
            ? $"Supprimé définitivement : {path} ({StorageFormat.Bytes(freed)} libérés)"
            : $"Mis à la Corbeille : {path} ({StorageFormat.Bytes(freed)} ; l’espace sera libéré en vidant la Corbeille)");
    }

    // ───────────────────────────── Divers ─────────────────────────────

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && !IsScanning)
        {
            ScanButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Status(string message) => StatusChanged?.Invoke(message);
}
