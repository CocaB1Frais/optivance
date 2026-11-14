using System.Windows;
using System.Windows.Controls;
using Optivance.Core;
using Optivance.Core.Storage;
using Optivance.Desktop.Storage;
using Optivance.Desktop.Ui;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

/// <summary>
/// Nettoyage en cartes : aperçu par catégorie, sélection, confirmation, puis suppression.
/// Standard = droits de l'utilisateur, Corbeille par défaut. Approfondi = composant
/// administrateur de la session, suppression définitive (pas de Corbeille pour ces emplacements).
/// </summary>
public sealed class CleaningPage : UserControl
{
    private sealed class Row
    {
        public required CleaningCategory Category { get; init; }
        public bool Selected { get; set; }
        public CleaningAnalysis? Analysis { get; set; }
        public long Bytes { get; set; } = -1;
        public long Files { get; set; }
        public string? Result { get; set; }
        public bool Busy { get; set; }
    }

    private static readonly string[] Tabs = ["Tout", "Standard", "Approfondi"];

    private readonly MainWindow _owner;
    private readonly List<Row> _rows;
    private readonly TabStrip _tabs = new(Tabs);
    private readonly CardPanel _cards = new();
    private readonly TextBlock _total = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _analyze = new() { Content = "Analyser", MinWidth = 110 };
    private readonly Button _clean = new() { Content = "Nettoyer la sélection", MinWidth = 170, IsEnabled = false };
    private readonly StackPanel _notice = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    private bool _permanent;
    private bool _busy;

    public CleaningPage(MainWindow owner)
    {
        _owner = owner;
        _rows = CleaningCatalog.All.Select(category => new Row { Category = category, Selected = category.CheckedByDefault }).ToList();
        Background = CardKit.Resource("WindowBrush");
        _status.Foreground = CardKit.Muted;
        _analyze.Style = (Style)Application.Current.FindResource("GhostButton");
        _clean.Style = (Style)Application.Current.FindResource("AccentButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_analyze, "CleaningAnalyze");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_clean, "CleaningRun");
        _analyze.Click += async (_, _) => await AnalyzeAsync();
        _clean.Click += async (_, _) => await CleanAsync();

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Nettoyage", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Fichiers temporaires et caches régénérés automatiquement. Vos documents ne sont jamais concernés.", Foreground = CardKit.Muted, Margin = new Thickness(1, 3, 0, 0) });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_total);
        tools.Children.Add(new Border { Width = 18 });
        tools.Children.Add(_analyze);
        tools.Children.Add(new Border { Width = 10 });
        tools.Children.Add(_clean);
        DockPanel.SetDock(tools, Dock.Right);
        header.Children.Add(tools);
        header.Children.Add(titles);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        toolbar.Children.Add(CardKit.FilterToggle("", "Supprimer définitivement (sans Corbeille)", on => { _permanent = on; Render(); }));
        _tabs.Selected += _ => Render();

        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(_tabs);
        top.Children.Add(toolbar);
        top.Children.Add(_notice);

        var root = new DockPanel { Margin = new Thickness(28, 18, 14, 16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });
        Content = root;

        _owner.Storage.SessionChanged += () => Dispatcher.BeginInvoke(() => { RefreshNotice(); Render(); });
        RefreshNotice();
        Render();
        _status.Text = "Cliquez sur « Analyser » pour estimer l’espace récupérable. Rien n’est supprimé sans votre confirmation.";
    }

    private bool AdminAvailable => _owner.Storage.Session is not null;

    private bool Usable(Row row) => !row.Category.RequiresAdmin || AdminAvailable;

    private IEnumerable<Row> Selected => _rows.Where(r => r.Selected && Usable(r));

    private void RefreshNotice()
    {
        _notice.Children.Clear();
        if (AdminAvailable)
        {
            return;
        }
        _notice.Children.Add(CardKit.Icon("", 13));
        _notice.Children.Add(new TextBlock { Text = "Autorisation administrateur non accordée : le nettoyage approfondi est indisponible pour cette session.", Foreground = CardKit.Muted, Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        if (MftScanClient.IsHelperPresent)
        {
            var authorize = new Button { Content = "Autoriser", Style = (Style)Application.Current.FindResource("GhostButton") };
            authorize.Click += async (_, _) => await _owner.Storage.InitializeSessionAsync();
            _notice.Children.Add(authorize);
        }
    }

    // ───────────────────────────── Affichage ─────────────────────────────

    private static string Describe(long bytes, long files, string unit = "fichier") =>
        files == 0 ? "Rien à nettoyer" : $"{StorageFormat.Bytes((ulong)bytes)} · {files:N0} {unit}{(files > 1 ? "s" : "")}";

    private void Render()
    {
        var tab = Tabs[_tabs.SelectedIndex];
        _cards.Children.Clear();
        foreach (var row in _rows.Where(r => tab == "Tout" || (tab == "Standard") == (r.Category.Level == CleaningLevel.Standard)))
        {
            _cards.Children.Add(BuildCard(row));
        }
        UpdateTotal();
    }

    private Border BuildCard(Row row)
    {
        var category = row.Category;
        var deep = category.RequiresAdmin;
        var bin = category.Id == CleaningCatalog.RecycleBinId;
        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
        chips.Children.Add(CardKit.Chip(deep ? "" : "", deep ? "Administrateur" : "Votre compte"));
        chips.Children.Add(CardKit.Chip(deep || bin || _permanent ? "" : "", deep || bin || _permanent ? "Définitif" : "Corbeille"));
        if (category.Targets.Any(t => t.MinimumAgeHours > 0))
        {
            var hours = category.Targets.Max(t => t.MinimumAgeHours);
            chips.Children.Add(CardKit.Chip("", hours >= 48 ? $"Plus de {hours / 24} jours" : $"Plus de {hours} h"));
        }

        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = category.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(chips);
        head.Children.Add(new TextBlock { Text = category.Description, Foreground = CardKit.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxHeight = 52, TextTrimming = TextTrimming.CharacterEllipsis });

        var size = row.Busy ? "…"
            : row.Result ?? (!Usable(row) ? "Autorisation requise"
            : row.Bytes < 0 ? "Non analysé"
            : bin ? Describe(row.Bytes, row.Files, "élément") : Describe(row.Bytes, row.Files));
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = size,
            FontSize = row.Bytes > 0 && row.Result is null ? 15 : 12.5,
            FontWeight = row.Bytes > 0 && row.Result is null ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = row.Bytes > 0 && row.Result is null ? CardKit.Text : CardKit.Muted
        });
        var toggle = CardKit.Switch(row.Selected, Usable(row) && !_busy);
        toggle.ToolTip = Usable(row) ? "Inclure dans le nettoyage" : "Autorisation administrateur requise";
        toggle.Click += (_, _) =>
        {
            row.Selected = toggle.IsChecked == true;
            UpdateTotal();
        };
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(toggle, Dock.Right);
        bottom.Children.Add(toggle);
        bottom.Children.Add(left);

        var panel = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        panel.Children.Add(head);
        return CardKit.Card(panel);
    }

    private void UpdateTotal()
    {
        var known = Selected.Where(r => r.Bytes >= 0).ToList();
        _total.Text = known.Count == 0 ? "" : $"{StorageFormat.Bytes((ulong)known.Sum(r => r.Bytes))} sélectionnés";
        _clean.IsEnabled = !_busy && known.Any(r => r.Bytes > 0 || (r.Category.Id == CleaningCatalog.RecycleBinId && r.Files > 0));
        _analyze.IsEnabled = !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Render();
    }

    // ───────────────────────────── Aperçu ─────────────────────────────

    private async Task AnalyzeAsync()
    {
        SetBusy(true);
        _status.Text = "Analyse en cours…";
        var session = _owner.Storage.Session;
        foreach (var row in _rows.Where(Usable))
        {
            row.Busy = true;
            row.Result = null;
            Render();
            try
            {
                if (row.Category.Id == CleaningCatalog.RecycleBinId)
                {
                    var bin = ShellOperations.QueryRecycleBin();
                    (row.Bytes, row.Files) = bin is { } value ? (value.Bytes, value.Items) : (0, 0);
                }
                else if (row.Category.RequiresAdmin)
                {
                    var report = await session!.CleanAsync(row.Category.Id, run: false, CancellationToken.None);
                    (row.Bytes, row.Files) = (report.Bytes, report.Files);
                }
                else
                {
                    var category = row.Category;
                    row.Analysis = await Task.Run(() => CleaningScanner.Analyze(category, DateTime.UtcNow));
                    (row.Bytes, row.Files) = (row.Analysis.Bytes, row.Analysis.Files.Count);
                }
            }
            catch (ScanEngineException exception)
            {
                row.Bytes = -1;
                row.Result = "Indisponible";
                _status.Text = exception.Message;
            }
            row.Busy = false;
        }
        var total = _rows.Where(r => r.Bytes > 0 && r.Category.Id != CleaningCatalog.RecycleBinId).Sum(r => r.Bytes);
        _status.Text = $"Analyse terminée : {StorageFormat.Bytes((ulong)total)} récupérables au total (hors Corbeille). Activez les catégories à nettoyer.";
        SetBusy(false);
    }

    // ───────────────────────────── Nettoyage ─────────────────────────────

    private async Task CleanAsync()
    {
        var chosen = Selected.Where(r => r.Bytes > 0 || (r.Category.Id == CleaningCatalog.RecycleBinId && r.Files > 0)).ToList();
        if (chosen.Count == 0)
        {
            return;
        }
        var permanent = _permanent;
        var hasDeep = chosen.Any(r => r.Category.RequiresAdmin);
        var hasBin = chosen.Any(r => r.Category.Id == CleaningCatalog.RecycleBinId);
        var message = permanent
            ? "Les fichiers seront supprimés définitivement."
            : "Les fichiers du nettoyage standard iront à la Corbeille (l’espace ne sera libéré qu’une fois la Corbeille vidée).";
        if (hasDeep) { message += "\nLes catégories approfondies sont supprimées définitivement."; }
        if (hasBin) { message += "\nLa Corbeille sera vidée : son contenu ne pourra plus être restauré."; }
        if (chosen.Any(r => r.Category.Id == "windows-old")) { message += "\nSupprimer Windows.old empêche tout retour à la version précédente de Windows."; }
        message += "\nLes fichiers en cours d’utilisation sont ignorés.";
        var details = chosen.Select(r => (r.Category.Name, r.Category.Id == CleaningCatalog.RecycleBinId ? Describe(r.Bytes, r.Files, "élément") : Describe(r.Bytes, r.Files))).ToList();
        var definitive = permanent || hasDeep || hasBin;
        if (!ConfirmDialog.Ask(Window.GetWindow(this), "Nettoyer la sélection ?", message, details, definitive ? "Supprimer définitivement" : "Nettoyer", danger: definitive))
        {
            return;
        }

        SetBusy(true);
        long freed = 0, deleted = 0, skipped = 0;
        foreach (var row in chosen)
        {
            row.Busy = true;
            Render();
            try
            {
                if (row.Category.Id == CleaningCatalog.RecycleBinId)
                {
                    var error = await Task.Run(ShellOperations.EmptyRecycleBin);
                    if (error is null) { freed += row.Bytes; deleted += row.Files; }
                    row.Result = error is null ? "Corbeille vidée" : "Échec";
                    App.History.Add(HistoryCategory.Cleaning, "Vider la Corbeille", row.Category.Name, error ?? Describe(row.Bytes, row.Files, "élément"), error is null);
                }
                else if (row.Category.RequiresAdmin)
                {
                    var session = _owner.Storage.Session ?? throw new ScanEngineException("session-closed", "Le composant administrateur n’est plus disponible.");
                    var report = await session.CleanAsync(row.Category.Id, run: true, CancellationToken.None);
                    freed += report.Freed;
                    deleted += report.Deleted;
                    skipped += report.Skipped + report.Refused;
                    row.Result = $"{StorageFormat.Bytes((ulong)report.Freed)} libérés";
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
                    row.Result = $"{StorageFormat.Bytes((ulong)outcome.FreedBytes)} {(permanent ? "libérés" : "à la Corbeille")}";
                    App.History.Add(HistoryCategory.Cleaning, permanent ? "Nettoyage définitif" : "Nettoyage (Corbeille)", row.Category.Name,
                        $"{StorageFormat.Bytes((ulong)outcome.FreedBytes)} · {outcome.Deleted:N0} supprimés · {outcome.Skipped:N0} ignorés");
                }
            }
            catch (ScanEngineException exception)
            {
                row.Result = "Échec";
                App.History.Add(HistoryCategory.Cleaning, "Nettoyage", row.Category.Name, exception.Message, success: false);
            }
            row.Busy = false;
            row.Bytes = -1;
            row.Analysis = null;
        }
        _status.Text = $"Nettoyage terminé : {StorageFormat.Bytes((ulong)freed)} traités, {deleted:N0} éléments supprimés"
            + (skipped > 0 ? $", {skipped:N0} ignorés (en cours d’utilisation ou protégés)." : ".")
            + " Relancez l’analyse pour mettre à jour les tailles.";
        SetBusy(false);
    }
}
