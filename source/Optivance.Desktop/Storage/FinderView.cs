using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Optivance.Core.Storage;

namespace Optivance.Desktop.Storage;

/// <summary>
/// Onglet « Gros fichiers et doublons » de Stockage : filtres (taille, date, extension), liste
/// à cocher, estimation de l'espace récupérable, suppression confirmée (Corbeille par défaut).
/// Travaille sur l'index de la dernière analyse : aucune nouvelle analyse du disque.
/// </summary>
public sealed class FinderView : UserControl
{
    public sealed class FinderRow : INotifyPropertyChanged
    {
        private bool _checked;
        public required int Id { get; init; }
        public required string Name { get; init; }
        public required string Folder { get; init; }
        public required ulong Bytes { get; init; }
        public string Size => StorageFormat.Bytes(Bytes);
        public required string Modified { get; init; }
        public string Group { get; init; } = "";
        public int GroupIndex { get; init; } = -1;
        public bool IsChecked
        {
            get => _checked;
            set { _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); Changed?.Invoke(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        internal Action? Changed;
    }

    private readonly RadioButton _largeMode = new() { Content = "Gros fichiers", IsChecked = true, GroupName = "finder", Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _duplicateMode = new() { Content = "Doublons", GroupName = "finder", Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _size = new() { MinWidth = 110 };
    private readonly ComboBox _date = new() { MinWidth = 150 };
    private readonly TextBox _extensions = new() { Width = 140, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Extensions séparées par des virgules (ex : mp4, iso, zip). Vide = toutes." };
    private readonly Button _search = new() { Content = "Rechercher", MinWidth = 110 };
    private readonly Button _cancel = new() { Content = "Annuler", Visibility = Visibility.Collapsed };
    private readonly TextBlock _progress = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly ListView _list = new();
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly Button _keepNewest = new() { Content = "Garder la plus récente", Visibility = Visibility.Collapsed };
    private readonly Button _recycle = new() { Content = "Mettre à la Corbeille", IsEnabled = false };
    private readonly Button _delete = new() { Content = "Supprimer définitivement", IsEnabled = false };
    private readonly List<FinderRow> _rows = [];
    private List<DuplicateGroup> _groups = [];
    private StorageTree? _tree;
    private CancellationTokenSource? _running;

    private static readonly (string Text, ulong Bytes)[] Sizes =
        [("Toutes tailles", 0), ("≥ 1 Mo", 1UL << 20), ("≥ 10 Mo", 10UL << 20), ("≥ 50 Mo", 50UL << 20), ("≥ 100 Mo", 100UL << 20), ("≥ 500 Mo", 500UL << 20), ("≥ 1 Go", 1UL << 30)];
    private static readonly (string Text, int Days, bool Older)[] Dates =
        [("Toutes les dates", 0, true), ("Modifiés il y a + de 1 mois", 30, true), ("Modifiés il y a + de 6 mois", 182, true), ("Modifiés il y a + de 1 an", 365, true), ("Modifiés il y a + de 3 ans", 1095, true), ("Modifiés ces 30 derniers jours", 30, false)];

    /// <summary>Suppression déléguée à la page Stockage (index, historique, treemap mis à jour).</summary>
    public Func<IReadOnlyList<int>, bool, Task>? DeleteRequested { get; set; }

    public FinderView()
    {
        var text = (Brush)Application.Current.FindResource("TextBrush");
        var muted = (Brush)Application.Current.FindResource("MutedTextBrush");
        _largeMode.Foreground = _duplicateMode.Foreground = text;
        _progress.Foreground = muted;
        foreach (var combo in new[] { _size, _date })
        {
            combo.ItemContainerStyle = (Style)Application.Current.FindResource("DarkComboBoxItem");
            combo.Margin = new Thickness(0, 0, 10, 0);
        }
        foreach (var (label, _) in Sizes) _size.Items.Add(label);
        foreach (var (label, _, _) in Dates) _date.Items.Add(label);
        _size.SelectedIndex = 3;
        _date.SelectedIndex = 0;
        _search.Style = (Style)Application.Current.FindResource("AccentButton");
        foreach (var button in new[] { _cancel, _keepNewest, _recycle })
        {
            button.Style = (Style)Application.Current.FindResource("GhostButton");
            button.Margin = new Thickness(8, 0, 0, 0);
        }
        _delete.Style = (Style)Application.Current.FindResource("GhostButton");
        _delete.Margin = new Thickness(8, 0, 0, 0);
        _delete.Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x6D, 0x6D));

        System.Windows.Automation.AutomationProperties.SetAutomationId(_duplicateMode, "FinderDuplicates");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_search, "FinderSearch");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_keepNewest, "FinderKeepNewest");
        _extensions.Height = 32;
        _search.Click += async (_, _) => await SearchAsync();
        _cancel.Click += (_, _) => _running?.Cancel();
        _largeMode.Checked += (_, _) => { _size.SelectedIndex = 3; ClearResults(); };
        _duplicateMode.Checked += (_, _) => { _size.SelectedIndex = 1; ClearResults(); };
        _keepNewest.Click += (_, _) => KeepNewest();
        _recycle.Click += async (_, _) => await DeleteAsync(permanent: false);
        _delete.Click += async (_, _) => await DeleteAsync(permanent: true);

        var filters = new WrapPanel { Margin = new Thickness(10, 10, 10, 8) };
        filters.Children.Add(_largeMode);
        filters.Children.Add(_duplicateMode);
        filters.Children.Add(_size);
        filters.Children.Add(_date);
        filters.Children.Add(new TextBlock { Text = "Extensions", Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        filters.Children.Add(_extensions);
        filters.Children.Add(new Border { Width = 10 });
        filters.Children.Add(_search);
        filters.Children.Add(_cancel);
        filters.Children.Add(_progress);

        _list.Style = (Style)Application.Current.FindResource("DataList");
        var view = new GridView { AllowsColumnReorder = false };
        view.Columns.Add(new GridViewColumn { Width = 34, CellTemplate = CheckTemplate() });
        view.Columns.Add(Column("Nom", 260, nameof(FinderRow.Name)));
        view.Columns.Add(Column("Taille", 90, nameof(FinderRow.Size)));
        view.Columns.Add(Column("Modifié", 120, nameof(FinderRow.Modified)));
        view.Columns.Add(Column("Groupe", 70, nameof(FinderRow.Group)));
        view.Columns.Add(Column("Dossier", 520, nameof(FinderRow.Folder)));
        _list.View = view;
        _list.ItemsSource = _rows;

        var actions = new DockPanel { Margin = new Thickness(10, 8, 10, 10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_keepNewest);
        buttons.Children.Add(_recycle);
        buttons.Children.Add(_delete);
        DockPanel.SetDock(buttons, Dock.Right);
        actions.Children.Add(buttons);
        actions.Children.Add(_summary);

        var root = new DockPanel();
        DockPanel.SetDock(filters, Dock.Top);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(filters);
        root.Children.Add(actions);
        root.Children.Add(_list);
        Content = root;
        UpdateSummary();
    }

    private static DataTemplate CheckTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(CheckBox));
        factory.SetBinding(ToggleButton_IsChecked, new Binding(nameof(FinderRow.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        factory.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        return new DataTemplate { VisualTree = factory };
    }

    private static readonly DependencyProperty ToggleButton_IsChecked = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    private static GridViewColumn Column(string header, double width, string path) =>
        new() { Header = header, Width = width, DisplayMemberBinding = new Binding(path) };

    /// <summary>Nouvelle analyse : résultats précédents effacés.</summary>
    public void SetTree(StorageTree? tree)
    {
        _running?.Cancel();
        _tree = tree;
        ClearResults();
    }

    /// <summary>Après une suppression : retire de la liste ce qui n'existe plus.</summary>
    public void RemoveDeleted()
    {
        if (_tree is null)
        {
            return;
        }
        _rows.RemoveAll(row => _tree.IsDeleted(row.Id));
        _groups = _groups.Select(g => g with { Ids = g.Ids.Where(id => !_tree.IsDeleted(id)).ToList() }).Where(g => g.Ids.Count > 1).ToList();
        _list.Items.Refresh();
        UpdateSummary();
    }

    private void ClearResults()
    {
        _rows.Clear();
        _groups = [];
        _list.Items.Refresh();
        _progress.Text = _tree is null ? "Lancez d’abord une analyse du disque." : "";
        _keepNewest.Visibility = _duplicateMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }

    private FileFilter CurrentFilter()
    {
        var (_, days, older) = Dates[Math.Max(0, _date.SelectedIndex)];
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return new FileFilter(
            Sizes[Math.Max(0, _size.SelectedIndex)].Bytes,
            days > 0 && older ? cutoff : null,
            days > 0 && !older ? cutoff : null,
            FileFilter.ParseExtensions(_extensions.Text));
    }

    private FinderRow MakeRow(int id, string group = "", int groupIndex = -1)
    {
        var tree = _tree!;
        var modified = tree.Modified(id);
        var row = new FinderRow
        {
            Id = id,
            Name = tree.Name(id),
            Folder = tree.GetPath(tree.Parent(id)),
            Bytes = tree.Size(id),
            Modified = modified > 0 ? DateTime.FromFileTimeUtc(modified).ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "",
            Group = group,
            GroupIndex = groupIndex
        };
        row.Changed = UpdateSummary;
        return row;
    }

    private async Task SearchAsync()
    {
        if (_tree is null)
        {
            _progress.Text = "Lancez d’abord une analyse du disque.";
            return;
        }
        var tree = _tree;
        var filter = CurrentFilter();
        ClearResults();
        if (_largeMode.IsChecked == true)
        {
            var ids = await Task.Run(() => FileFinder.LargeFiles(tree, filter));
            _rows.AddRange(ids.Select(id => MakeRow(id)));
            _progress.Text = ids.Count >= 2000 ? "2 000 plus gros fichiers affichés (affinez les filtres)." : $"{ids.Count:N0} fichier(s).";
        }
        else
        {
            _running = new CancellationTokenSource();
            _search.IsEnabled = false;
            _cancel.Visibility = Visibility.Visible;
            var progress = new Progress<(long Done, long Total)>(p => _progress.Text = p.Total == 0 ? "" : $"Comparaison du contenu… {100.0 * p.Done / p.Total:0} %");
            try
            {
                _progress.Text = "Recherche des fichiers de même taille…";
                _groups = await Task.Run(() => FileFinder.DuplicatesAsync(tree, filter, progress, _running.Token));
                var index = 0;
                foreach (var group in _groups)
                {
                    index++;
                    _rows.AddRange(group.Ids.OrderByDescending(tree.Modified).Select(id => MakeRow(id, $"#{index}", index)));
                }
                var recoverable = _groups.Aggregate(0UL, (sum, g) => sum + g.Recoverable);
                _progress.Text = _groups.Count == 0 ? "Aucun doublon trouvé." : $"{_groups.Count:N0} groupe(s) de doublons · {StorageFormat.Bytes(recoverable)} récupérables en gardant une copie de chaque.";
            }
            catch (OperationCanceledException)
            {
                _progress.Text = "Recherche annulée.";
            }
            finally
            {
                _search.IsEnabled = true;
                _cancel.Visibility = Visibility.Collapsed;
            }
        }
        _list.Items.Refresh();
        UpdateSummary();
    }

    private void KeepNewest()
    {
        if (_tree is null)
        {
            return;
        }
        var selected = FileFinder.AllButNewest(_tree, _groups).ToHashSet();
        foreach (var row in _rows)
        {
            row.IsChecked = selected.Contains(row.Id);
        }
    }

    private void UpdateSummary()
    {
        var chosen = _rows.Where(r => r.IsChecked).ToList();
        var bytes = chosen.Aggregate(0UL, (sum, r) => sum + r.Bytes);
        _summary.Text = chosen.Count == 0
            ? (_rows.Count == 0 ? "" : "Cochez les fichiers à supprimer.")
            : $"{chosen.Count:N0} sélectionné(s) · {StorageFormat.Bytes(bytes)} récupérables";
        _recycle.IsEnabled = _delete.IsEnabled = chosen.Count > 0 && DeleteRequested is not null;
    }

    private async Task DeleteAsync(bool permanent)
    {
        var chosen = _rows.Where(r => r.IsChecked).ToList();
        if (chosen.Count == 0 || DeleteRequested is null)
        {
            return;
        }
        // Doublons : au moins une copie de chaque groupe est toujours conservée.
        var emptied = chosen.Where(r => r.GroupIndex >= 0).GroupBy(r => r.GroupIndex)
            .Count(g => g.Count() >= _rows.Count(r => r.GroupIndex == g.Key));
        if (emptied > 0)
        {
            ConfirmDialog.Inform(Window.GetWindow(this), "Gardez au moins une copie",
                $"Dans {emptied} groupe(s), toutes les copies sont cochées : vous perdriez ce fichier. Décochez au moins une copie par groupe (ou utilisez « Garder la plus récente »).");
            return;
        }
        var bytes = chosen.Aggregate(0UL, (sum, r) => sum + r.Bytes);
        var details = chosen.Take(8).Select(r => (r.Name, r.Size)).ToList();
        if (chosen.Count > 8)
        {
            details.Add(("…", $"et {chosen.Count - 8:N0} autre(s)"));
        }
        var message = permanent
            ? $"{chosen.Count:N0} fichier(s), {StorageFormat.Bytes(bytes)}, seront effacés sans passer par la Corbeille. Cette action est irréversible."
            : $"{chosen.Count:N0} fichier(s), {StorageFormat.Bytes(bytes)}, iront à la Corbeille (restaurables ; l’espace est libéré en vidant la Corbeille).";
        if (!ConfirmDialog.Ask(Window.GetWindow(this), permanent ? "Supprimer définitivement ?" : "Mettre à la Corbeille ?", message, details,
                permanent ? "Supprimer définitivement" : "Mettre à la Corbeille", danger: permanent))
        {
            return;
        }
        await DeleteRequested(chosen.Select(r => r.Id).ToList(), permanent);
        RemoveDeleted();
    }
}
