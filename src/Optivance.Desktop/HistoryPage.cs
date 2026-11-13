using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Optivance.Core;

namespace Optivance.Desktop;

/// <summary>Historique des actions de l'utilisateur (suppressions, nettoyages, modifications, paramètres).</summary>
public sealed class HistoryPage : UserControl
{
    private readonly ListView _list = new();
    private readonly ComboBox _filter = new() { MinWidth = 170 };
    private readonly TextBox _search = new() { Width = 240 };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private List<HistoryEntry> _entries = [];

    private static readonly (string Label, HistoryCategory? Category)[] Filters =
    [
        ("Toutes les actions", null),
        ("Stockage", HistoryCategory.Storage),
        ("Nettoyage", HistoryCategory.Cleaning),
        ("Optimisation", HistoryCategory.Optimization),
        ("Monitoring", HistoryCategory.Monitoring),
        ("Paramètres", HistoryCategory.Settings),
        ("Application", HistoryCategory.Application)
    ];

    public HistoryPage()
    {
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        var muted = (Brush)Application.Current.FindResource("MutedTextBrush");
        _count.Foreground = muted;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Historique", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Toutes les actions réalisées avec OPTIVANCE : suppressions, nettoyages, modifications et paramètres.", Foreground = muted, Margin = new Thickness(1, 3, 0, 0) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var filter in Filters)
        {
            _filter.Items.Add(filter.Label);
        }
        _filter.SelectedIndex = 0;
        _filter.ItemContainerStyle = (Style)Application.Current.FindResource("DarkComboBoxItem");
        _filter.SelectionChanged += (_, _) => Apply();
        _search.TextChanged += (_, _) => Apply();
        _search.ToolTip = "Rechercher dans l’historique";
        tools.Children.Add(_count);
        tools.Children.Add(new Border { Width = 14 });
        tools.Children.Add(_search);
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(_filter);
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(titles);

        _list.Style = (Style)Application.Current.FindResource("DataList");
        var view = new GridView { AllowsColumnReorder = false };
        view.Columns.Add(Column("Date", 140, nameof(Row.Time)));
        view.Columns.Add(Column("Domaine", 110, nameof(Row.Category)));
        view.Columns.Add(Column("Action", 190, nameof(Row.Action)));
        view.Columns.Add(Column("Élément", 520, nameof(Row.Target)));
        view.Columns.Add(Column("Détails", 260, nameof(Row.Details)));
        view.Columns.Add(Column("Résultat", 90, nameof(Row.Result)));
        _list.View = view;

        var card = new Border { Style = (Style)Application.Current.FindResource("Card"), Child = _list };
        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 20) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(card);
        Content = root;

        Loaded += (_, _) => Reload();
        App.History.Added += entry => Dispatcher.BeginInvoke(Reload);
    }

    private static GridViewColumn Column(string header, double width, string path) => new()
    {
        Header = header,
        Width = width,
        DisplayMemberBinding = new System.Windows.Data.Binding(path)
    };

    private void Reload()
    {
        _entries = App.History.Load(App.Settings.HistoryRetentionDays);
        Apply();
    }

    private void Apply()
    {
        var category = _filter.SelectedIndex >= 0 ? Filters[_filter.SelectedIndex].Category : null;
        var search = _search.Text.Trim();
        var rows = _entries
            .Where(entry => category is null || entry.Category == category)
            .Where(entry => search.Length == 0
                || entry.Target.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Action.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Details.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new Row(entry))
            .ToList();
        _list.ItemsSource = rows;
        _count.Text = rows.Count == 0 ? "Aucune action" : $"{rows.Count} action(s)";
    }

    private sealed class Row(HistoryEntry entry)
    {
        public string Time => entry.Time.LocalDateTime.ToString("dd/MM/yyyy HH:mm:ss");
        public string Category => entry.Category switch
        {
            HistoryCategory.Storage => "Stockage",
            HistoryCategory.Cleaning => "Nettoyage",
            HistoryCategory.Optimization => "Optimisation",
            HistoryCategory.Monitoring => "Monitoring",
            HistoryCategory.Settings => "Paramètres",
            _ => "Application"
        };
        public string Action => entry.Action;
        public string Target => entry.Target;
        public string Details => entry.Details;
        public string Result => entry.Success ? "Réussi" : "Échec";
        public override string ToString() => $"{Action} {Target}";
    }
}
