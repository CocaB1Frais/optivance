using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Optivance.Core;
using Optivance.Core.Storage;
using Optivance.Desktop.Storage;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

/// <summary>
/// Nettoyage : aperçu par catégorie, sélection, confirmation, puis suppression.
/// Standard = droits de l'utilisateur, Corbeille par défaut. Approfondi = composant
/// administrateur de la session, suppression définitive (Windows ne propose pas de Corbeille
/// pour ces emplacements système).
/// </summary>
public sealed class CleaningPage : UserControl
{
    private sealed class Row
    {
        public required CleaningCategory Category { get; init; }
        public required CheckBox Box { get; init; }
        public required TextBlock Size { get; init; }
        public CleaningAnalysis? Analysis { get; set; }
        public long Bytes { get; set; } = -1;
        public long Files { get; set; }
    }

    private readonly MainWindow _owner;
    private readonly List<Row> _rows = [];
    private readonly TextBlock _total = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _analyze = new() { Content = "Analyser", MinWidth = 110 };
    private readonly Button _clean = new() { Content = "Nettoyer la sélection", MinWidth = 170, IsEnabled = false };
    private readonly CheckBox _permanent = new() { Content = "Supprimer définitivement (sans passer par la Corbeille)", Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _deepNotice = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Brush _muted;
    private bool _busy;

    public CleaningPage(MainWindow owner)
    {
        _owner = owner;
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        _muted = (Brush)Application.Current.FindResource("MutedTextBrush");
        _status.Foreground = _muted;
        _permanent.Foreground = (Brush)Application.Current.FindResource("TextBrush");
        _analyze.Style = (Style)Application.Current.FindResource("GhostButton");
        _clean.Style = (Style)Application.Current.FindResource("AccentButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_analyze, "CleaningAnalyze");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_clean, "CleaningRun");
        _analyze.Click += async (_, _) => await AnalyzeAsync();
        _clean.Click += async (_, _) => await CleanAsync();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Nettoyage", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Fichiers temporaires et caches régénérés automatiquement. Vos documents ne sont jamais concernés.", Foreground = _muted, Margin = new Thickness(1, 3, 0, 0) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_total);
        tools.Children.Add(new Border { Width = 18 });
        tools.Children.Add(_analyze);
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(_clean);
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(titles);

        var standard = Section("Nettoyage standard", "Avec vos droits habituels. Les fichiers vont à la Corbeille, sauf option contraire.", CleaningLevel.Standard);
        var deep = Section("Nettoyage approfondi", "Emplacements de Windows : autorisation administrateur requise ; suppression définitive.", CleaningLevel.Deep, _deepNotice);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(deep, 2);
        columns.Children.Add(standard);
        columns.Children.Add(deep);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(_permanent);
        footer.Children.Add(_status);

        var body = new StackPanel();
        body.Children.Add(columns);
        body.Children.Add(footer);
        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 20) };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        _owner.Storage.SessionChanged += () => Dispatcher.BeginInvoke(RefreshDeepNotice);
        RefreshDeepNotice();
        UpdateTotal();
        _status.Text = "Cliquez sur « Analyser » pour estimer l’espace récupérable. Rien n’est supprimé sans votre confirmation.";
    }

    private Border Section(string title, string subtitle, CleaningLevel level, UIElement? notice = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = _muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) });
        if (notice is not null)
        {
            panel.Children.Add(notice);
        }
        foreach (var category in CleaningCatalog.All.Where(c => c.Level == level))
        {
            var box = new CheckBox { IsChecked = category.CheckedByDefault, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 10, 0) };
            var size = new TextBlock { Text = "—", Foreground = _muted, MinWidth = 120, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 2, 0, 0) };
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = category.Name, FontWeight = FontWeights.SemiBold });
            texts.Children.Add(new TextBlock { Text = category.Description, Foreground = _muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 1, 0, 0) });
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(box, Dock.Left);
            DockPanel.SetDock(size, Dock.Right);
            line.Children.Add(box);
            line.Children.Add(size);
            line.Children.Add(texts);
            panel.Children.Add(line);
            var row = new Row { Category = category, Box = box, Size = size };
            box.Click += (_, _) => UpdateTotal();
            _rows.Add(row);
        }
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(18, 16, 18, 6), Child = panel, VerticalAlignment = VerticalAlignment.Top };
    }

    private void RefreshDeepNotice()
    {
        _deepNotice.Children.Clear();
        var available = _owner.Storage.Session is not null;
        foreach (var row in _rows.Where(r => r.Category.RequiresAdmin))
        {
            row.Box.IsEnabled = available && !_busy;
            if (!available)
            {
                row.Size.Text = "Autorisation requise";
            }
            else if (row.Bytes < 0)
            {
                row.Size.Text = "—";
            }
        }
        if (available)
        {
            return;
        }
        _deepNotice.Children.Add(new TextBlock { Text = "L’autorisation administrateur n’a pas été accordée pour cette session.", Foreground = _muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 330 });
        if (MftScanClient.IsHelperPresent)
        {
            var authorize = new Button { Content = "Autoriser", Style = (Style)Application.Current.FindResource("GhostButton") };
            authorize.Click += async (_, _) => await _owner.Storage.InitializeSessionAsync();
            _deepNotice.Children.Add(authorize);
        }
    }

    private IEnumerable<Row> Selected => _rows.Where(r => r.Box.IsChecked == true && r.Box.IsEnabled);

    private void UpdateTotal()
    {
        var known = Selected.Where(r => r.Bytes >= 0).ToList();
        _total.Text = known.Count == 0 ? "" : $"{StorageFormat.Bytes((ulong)known.Sum(r => r.Bytes))} sélectionnés";
        _clean.IsEnabled = !_busy && known.Any(r => r.Bytes > 0 || (r.Category.Id == CleaningCatalog.RecycleBinId && r.Files > 0));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _analyze.IsEnabled = !busy;
        _permanent.IsEnabled = !busy;
        foreach (var row in _rows)
        {
            row.Box.IsEnabled = !busy && (!row.Category.RequiresAdmin || _owner.Storage.Session is not null);
        }
        UpdateTotal();
    }

    private static string Describe(long bytes, long files) =>
        files == 0 ? "Rien à nettoyer" : $"{StorageFormat.Bytes((ulong)bytes)} · {files:N0} fichier{(files > 1 ? "s" : "")}";

    // ───────────────────────────── Aperçu ─────────────────────────────

    private async Task AnalyzeAsync()
    {
        SetBusy(true);
        _status.Text = "Analyse en cours…";
        var session = _owner.Storage.Session;
        foreach (var row in _rows)
        {
            if (row.Category.RequiresAdmin && session is null)
            {
                continue;
            }
            row.Size.Text = "Analyse…";
            try
            {
                if (row.Category.Id == CleaningCatalog.RecycleBinId)
                {
                    var bin = ShellOperations.QueryRecycleBin();
                    (row.Bytes, row.Files) = bin is { } value ? (value.Bytes, value.Items) : (0, 0);
                    row.Size.Text = bin is null ? "Indisponible" : Describe(row.Bytes, row.Files).Replace("fichier", "élément");
                }
                else if (row.Category.RequiresAdmin)
                {
                    var report = await session!.CleanAsync(row.Category.Id, run: false, CancellationToken.None);
                    (row.Bytes, row.Files) = (report.Bytes, report.Files);
                    row.Size.Text = Describe(row.Bytes, row.Files);
                }
                else
                {
                    var category = row.Category;
                    row.Analysis = await Task.Run(() => CleaningScanner.Analyze(category, DateTime.UtcNow));
                    (row.Bytes, row.Files) = (row.Analysis.Bytes, row.Analysis.Files.Count);
                    row.Size.Text = Describe(row.Bytes, row.Files);
                }
            }
            catch (ScanEngineException exception)
            {
                row.Bytes = -1;
                row.Size.Text = "Indisponible";
                _status.Text = exception.Message;
            }
            UpdateTotal();
        }
        var total = _rows.Where(r => r.Bytes > 0 && r.Category.Id != CleaningCatalog.RecycleBinId).Sum(r => r.Bytes);
        _status.Text = $"Analyse terminée : {StorageFormat.Bytes((ulong)total)} récupérables au total (hors Corbeille). Cochez les catégories à nettoyer.";
        SetBusy(false);
        RefreshDeepNotice();
    }

    // ───────────────────────────── Nettoyage ─────────────────────────────

    private async Task CleanAsync()
    {
        var chosen = Selected.Where(r => r.Bytes > 0 || (r.Category.Id == CleaningCatalog.RecycleBinId && r.Files > 0)).ToList();
        if (chosen.Count == 0)
        {
            return;
        }
        var permanent = _permanent.IsChecked == true;
        var hasDeep = chosen.Any(r => r.Category.RequiresAdmin);
        var hasBin = chosen.Any(r => r.Category.Id == CleaningCatalog.RecycleBinId);
        var hasWindowsOld = chosen.Any(r => r.Category.Id == "windows-old");
        var message = permanent
            ? "Les fichiers seront supprimés définitivement."
            : "Les fichiers du nettoyage standard iront à la Corbeille (l’espace ne sera libéré qu’une fois la Corbeille vidée).";
        if (hasDeep)
        {
            message += "\nLes catégories approfondies sont supprimées définitivement.";
        }
        if (hasBin)
        {
            message += "\nLa Corbeille sera vidée : son contenu ne pourra plus être restauré.";
        }
        if (hasWindowsOld)
        {
            message += "\nSupprimer Windows.old empêche tout retour à la version précédente de Windows.";
        }
        message += "\nLes fichiers en cours d’utilisation sont ignorés.";
        var details = chosen.Select(r => (r.Category.Name, r.Size.Text)).ToList();
        if (!ConfirmDialog.Ask(Window.GetWindow(this), "Nettoyer la sélection ?", message, details,
                permanent || hasDeep || hasBin ? "Supprimer définitivement" : "Nettoyer", danger: permanent || hasDeep || hasBin))
        {
            return;
        }

        SetBusy(true);
        long freed = 0;
        long deleted = 0;
        long skipped = 0;
        foreach (var row in chosen)
        {
            row.Size.Text = "Nettoyage…";
            try
            {
                if (row.Category.Id == CleaningCatalog.RecycleBinId)
                {
                    var error = await Task.Run(ShellOperations.EmptyRecycleBin);
                    var success = error is null;
                    if (success)
                    {
                        freed += row.Bytes;
                        deleted += row.Files;
                    }
                    row.Size.Text = success ? "Vidée" : "Échec";
                    App.History.Add(HistoryCategory.Cleaning, "Vider la Corbeille", row.Category.Name, error ?? Describe(row.Bytes, row.Files).Replace("fichier", "élément"), success);
                }
                else if (row.Category.RequiresAdmin)
                {
                    var session = _owner.Storage.Session ?? throw new ScanEngineException("session-closed", "Le composant administrateur n’est plus disponible.");
                    var report = await session.CleanAsync(row.Category.Id, run: true, CancellationToken.None);
                    freed += report.Freed;
                    deleted += report.Deleted;
                    skipped += report.Skipped + report.Refused;
                    row.Size.Text = $"{StorageFormat.Bytes((ulong)report.Freed)} libérés";
                    App.History.Add(HistoryCategory.Cleaning, "Nettoyage approfondi", row.Category.Name,
                        $"{StorageFormat.Bytes((ulong)report.Freed)} libérés · {report.Deleted:N0} supprimés · {report.Skipped + report.Refused:N0} ignorés", report.State == "completed");
                }
                else if (row.Analysis is { } analysis)
                {
                    Action<IReadOnlyList<string>>? recycle = permanent ? null : ShellOperations.RecycleFiles;
                    var outcome = await Task.Run(() => CleaningScanner.Execute(analysis, DateTime.UtcNow, recycle));
                    freed += outcome.FreedBytes;
                    deleted += outcome.Deleted;
                    skipped += outcome.Skipped;
                    row.Size.Text = $"{StorageFormat.Bytes((ulong)outcome.FreedBytes)} {(permanent ? "libérés" : "à la Corbeille")}";
                    App.History.Add(HistoryCategory.Cleaning, permanent ? "Nettoyage définitif" : "Nettoyage (Corbeille)", row.Category.Name,
                        $"{StorageFormat.Bytes((ulong)outcome.FreedBytes)} · {outcome.Deleted:N0} supprimés · {outcome.Skipped:N0} ignorés");
                }
            }
            catch (ScanEngineException exception)
            {
                row.Size.Text = "Échec";
                App.History.Add(HistoryCategory.Cleaning, "Nettoyage", row.Category.Name, exception.Message, success: false);
            }
            row.Bytes = -1;
            row.Analysis = null;
        }
        _status.Text = $"Nettoyage terminé : {StorageFormat.Bytes((ulong)freed)} traités, {deleted:N0} éléments supprimés"
            + (skipped > 0 ? $", {skipped:N0} ignorés (en cours d’utilisation ou protégés)." : ".")
            + " Relancez l’analyse pour mettre à jour les tailles.";
        SetBusy(false);
    }
}
