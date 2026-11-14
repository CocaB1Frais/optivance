using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Optivance.Core;
using Optivance.Desktop.Storage;
using Optivance.Desktop.Ui;
using Optivance.Infrastructure;
using Optivance.Infrastructure.Optimization;

namespace Optivance.Desktop;

/// <summary>
/// Optimisation, présentée en cartes : onglets par thème, recherche, filtre de risque, masquage
/// des réglages appliqués. Chaque modification est précédée d'un avertissement si elle comporte
/// un risque, sauvegardée (valeur d'origine) et inscrite dans l'historique ; tout est restaurable.
/// </summary>
public sealed class OptimizationPage : UserControl
{
    private sealed class Item
    {
        public required string Tab { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required (string Glyph, string Text)[] Chips { get; init; }
        public TweakRisk? Risk { get; init; }
        public string Consequence { get; init; } = "";
        public bool IsOn { get; set; }
        public bool CanToggle { get; init; } = true;
        public string? Disabled { get; init; }
        public string? State { get; init; }
        /// <summary>Activer (vrai) ou revenir en arrière (faux) ; renvoie l'erreur éventuelle.</summary>
        public required Func<bool, Task<string?>> Apply { get; init; }
        public bool RefreshAfter { get; init; }
    }

    private static readonly string[] Tabs = ["Tout", "Performances", "Jeux", "Confidentialité", "Alimentation", "Démarrage", "Services", "Tâches planifiées"];
    private static readonly (string Glyph, string Text) Account = ("", "Votre compte");
    private static readonly (string Glyph, string Text) Admin = ("", "Administrateur");

    private readonly MainWindow _owner;
    private readonly OptimizationBackup _backup = new();
    private readonly TabStrip _tabs = new(Tabs);
    private readonly CardPanel _cards = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox _risk = new() { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly StackPanel _notice = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
    private List<Item> _items = [];
    private Dictionary<string, DateTimeOffset> _applied = [];
    private string _search = "";
    private bool _hideApplied;
    private bool _refreshing;

    public OptimizationPage(MainWindow owner)
    {
        _owner = owner;
        Background = CardKit.Resource("WindowBrush");
        _status.Foreground = CardKit.Muted;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Optimisation", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Chaque réglage est sauvegardé avant modification et réversible. Les réglages risqués affichent leurs conséquences avant d’être appliqués.", Foreground = CardKit.Muted, Margin = new Thickness(1, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        var restoreAll = new Button { Content = "Tout restaurer", Style = (Style)Application.Current.FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 130 };
        restoreAll.Click += async (_, _) => await RestoreAllAsync();
        DockPanel.SetDock(restoreAll, Dock.Right);
        header.Children.Add(restoreAll);
        header.Children.Add(titles);

        foreach (var label in new[] { "Tous les niveaux", "Sans risque", "Modéré", "Risqué" })
        {
            _risk.Items.Add(label);
        }
        _risk.SelectedIndex = 0;
        _risk.ItemContainerStyle = (Style)Application.Current.FindResource("DarkComboBoxItem");
        _risk.SelectionChanged += (_, _) => Render();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        toolbar.Children.Add(CardKit.SearchBox("Rechercher…", text => { _search = text; Render(); }));
        toolbar.Children.Add(_risk);
        toolbar.Children.Add(CardKit.FilterToggle("", "Masquer les réglages appliqués", on => { _hideApplied = on; Render(); }));
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

        Loaded += async (_, _) => await RefreshAsync();
        _owner.Storage.SessionChanged += () => Dispatcher.BeginInvoke(async () => await RefreshAsync());
    }

    private MftSession? Session => _owner.Storage.Session;

    // ───────────────────────────── Données ─────────────────────────────

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;
        _status.Text = "Lecture de l’état actuel…";
        var services = OptimizationCatalog.Services.Select(s => (s, SystemState.ServiceMode(s.Name))).ToList();
        var tasks = await Task.Run(() => OptimizationCatalog.Tasks.Select(t => (t, SystemState.TaskEnabled(t.Path))).ToList());
        var startup = await Task.Run(StartupApps.List);
        var plans = PowerPlans.List();
        _applied = App.History.Load(App.Settings.HistoryRetentionDays)
            .Where(e => e.Category == HistoryCategory.Optimization && e.Success)
            .GroupBy(e => e.Target)
            .Select(g => g.OrderByDescending(e => e.Time).First())
            .Where(e => e.Action == "Réglage appliqué")
            .ToDictionary(e => e.Target, e => e.Time);

        var items = new List<Item>();
        items.AddRange(UserTweaks.All.Select(UserItem));
        items.AddRange(plans.Select(PowerItem));
        items.AddRange(startup.Select(StartupItem));
        items.AddRange(services.Where(s => s.Item2 != "missing").Select(s => ServiceItem(s.Item1, s.Item2)));
        items.AddRange(tasks.Where(t => t.Item2 is not null || Session is not null).Select(t => TaskItem(t.Item1, t.Item2)));
        _items = items;

        _notice.Children.Clear();
        if (Session is null)
        {
            _notice.Children.Add(CardKit.Icon("", 13));
            _notice.Children.Add(new TextBlock { Text = "Autorisation administrateur non accordée : services, tâches et démarrage « tous les utilisateurs » sont en lecture seule.", Foreground = CardKit.Muted, Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            if (MftScanClient.IsHelperPresent)
            {
                var authorize = new Button { Content = "Autoriser", Style = (Style)Application.Current.FindResource("GhostButton") };
                authorize.Click += async (_, _) => await _owner.Storage.InitializeSessionAsync();
                _notice.Children.Add(authorize);
            }
        }
        Render();
        _status.Text = _backup.Values.Count == 0 ? "Aucun réglage modifié par OPTIVANCE pour l’instant." : $"{_backup.Values.Count} valeur(s) d’origine sauvegardée(s) : « Tout restaurer » revient à l’état d’avant OPTIVANCE.";
        _refreshing = false;
    }

    private static string TabOf(string tweakId) => tweakId switch
    {
        "game-mode" or "game-dvr" or "mouse-precision" => "Jeux",
        "advertising-id" or "suggestions" => "Confidentialité",
        _ => "Performances"
    };

    private static string GlyphOf(string tab) => tab switch
    {
        "Jeux" => "",
        "Confidentialité" => "",
        "Alimentation" => "",
        "Démarrage" => "",
        "Services" => "",
        "Tâches planifiées" => "",
        _ => ""
    };

    private Item UserItem(UserTweak tweak)
    {
        var tab = TabOf(tweak.Id);
        return new Item
        {
            Tab = tab, Title = tweak.Title, Description = tweak.Description, Risk = tweak.Risk, Consequence = tweak.Consequence,
            Chips = [Account, (GlyphOf(tab), tab)], IsOn = UserTweaks.IsOptimized(tweak),
            Apply = enabled =>
            {
                if (enabled) { UserTweaks.Apply(tweak, _backup); } else { UserTweaks.Restore(tweak, _backup); }
                return Task.FromResult<string?>(null);
            }
        };
    }

    private Item PowerItem(PowerPlan plan) => new()
    {
        Tab = "Alimentation", Title = plan.Name,
        Description = plan.Name.Contains("élev", StringComparison.OrdinalIgnoreCase) || plan.Name.Contains("High", StringComparison.OrdinalIgnoreCase)
            ? "Réactivité maximale ; sur un portable, consommation et chaleur plus élevées."
            : "Mode d’alimentation de Windows. Un seul mode est actif à la fois.",
        Chips = [Account, ("", "Alimentation")], IsOn = plan.Active, RefreshAfter = true,
        State = plan.Active ? "Actif" : null,
        Apply = enabled =>
        {
            if (!enabled)
            {
                return Task.FromResult<string?>("activez un autre mode pour remplacer celui-ci.");
            }
            if (PowerPlans.Active() is { } previous)
            {
                _backup.Remember("power", previous.ToString());
            }
            return Task.FromResult(PowerPlans.SetActive(plan.Id) ? null : "Windows a refusé le changement de mode.");
        }
    };

    private Item StartupItem(StartupApp app)
    {
        var canToggle = app.CanToggle && (!app.RequiresAdmin || (Session is not null && StartupApps.CanSendToHelper(app)));
        var key = $"startup:{app.Location}|{app.Name}";
        return new Item
        {
            Tab = "Démarrage", Title = app.Name, Description = app.Command,
            Chips = [app.RequiresAdmin ? Admin : Account, ("", app.Location is StartupLocation.UserFolder or StartupLocation.CommonFolder ? "Dossier Démarrage" : "Lancement auto")],
            IsOn = app.Enabled, CanToggle = canToggle,
            Disabled = canToggle ? null : app.Location == StartupLocation.CommonFolder ? "Dossier Démarrage commun : à modifier depuis l’Explorateur." : "Autorisation administrateur requise.",
            State = app.Enabled ? "Se lance avec Windows" : "Désactivée",
            Apply = async enabled =>
            {
                _backup.Remember(key, app.Enabled ? "true" : "false");
                if (app.RequiresAdmin)
                {
                    return await OptimizationRestore.TuneAsync(Session, StartupApps.MachineCommand(app, enabled));
                }
                StartupApps.SetUserEnabled(app, enabled);
                return null;
            }
        };
    }

    private Item ServiceItem(ServiceTweak service, string mode)
    {
        var key = $"svc:{service.Name}";
        return new Item
        {
            Tab = "Services", Title = $"Désactiver : {service.Title}", Description = service.Description, Risk = service.Risk, Consequence = service.Consequence,
            Chips = [Admin, ("", "Service")], IsOn = mode == "disabled", CanToggle = Session is not null,
            Disabled = Session is null ? "Autorisation administrateur requise." : null,
            State = OptimizationCatalog.StartModeLabel(mode),
            Apply = async disable =>
            {
                if (disable)
                {
                    _backup.Remember(key, SystemState.ServiceMode(service.Name));
                    return await OptimizationRestore.TuneAsync(Session, OptimizationRestore.ServiceCommand(service.Name, "disabled"));
                }
                var original = _backup.TryGet(key, out var saved) && saved is not ("disabled" or "unknown") ? saved : "demand";
                var error = await OptimizationRestore.TuneAsync(Session, OptimizationRestore.ServiceCommand(service.Name, original));
                if (error is null) { _backup.Forget(key); }
                return error;
            }
        };
    }

    private Item TaskItem(TaskTweak task, bool? enabled)
    {
        var key = $"task:{task.Id}";
        return new Item
        {
            Tab = "Tâches planifiées", Title = $"Désactiver : {task.Title}", Description = task.Consequence, Risk = task.Risk, Consequence = task.Consequence,
            Chips = [Admin, ("", "Tâche planifiée")], IsOn = enabled == false, CanToggle = Session is not null,
            Disabled = Session is null ? "Autorisation administrateur requise." : null,
            State = enabled switch { true => "Active", false => "Désactivée", null => "État inconnu" },
            Apply = async disable =>
            {
                if (disable) { _backup.Remember(key, enabled == false ? "false" : "true"); }
                var reenable = !disable && (!_backup.TryGet(key, out var saved) || saved == "true");
                var error = await OptimizationRestore.TuneAsync(Session, OptimizationRestore.TaskCommand(task.Id, reenable));
                if (error is null && !disable) { _backup.Forget(key); }
                return error;
            }
        };
    }

    // ───────────────────────────── Affichage ─────────────────────────────

    private void Render()
    {
        var tab = Tabs[_tabs.SelectedIndex];
        TweakRisk? risk = _risk.SelectedIndex switch { 1 => TweakRisk.Safe, 2 => TweakRisk.Moderate, 3 => TweakRisk.Risky, _ => null };
        var visible = _items.Where(item =>
            (tab == "Tout" || item.Tab == tab)
            && (risk is null || item.Risk == risk)
            && (!_hideApplied || !item.IsOn)
            && (_search.Length == 0 || item.Title.Contains(_search, StringComparison.CurrentCultureIgnoreCase) || item.Description.Contains(_search, StringComparison.CurrentCultureIgnoreCase)));
        _cards.Children.Clear();
        foreach (var item in visible)
        {
            _cards.Children.Add(BuildCard(item));
        }
        if (_cards.Children.Count == 0)
        {
            _cards.Children.Add(new TextBlock { Text = "Aucun réglage ne correspond aux filtres.", Foreground = CardKit.Muted, Margin = new Thickness(2, 10, 0, 0) });
        }
    }

    private Border BuildCard(Item item)
    {
        var panel = new DockPanel { LastChildFill = true };

        var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
        foreach (var (glyph, text) in item.Chips)
        {
            chips.Children.Add(CardKit.Chip(glyph, text));
        }

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var toggle = CardKit.Switch(item.IsOn, item.CanToggle);
        toggle.ToolTip = item.Disabled;
        DockPanel.SetDock(toggle, Dock.Right);
        bottom.Children.Add(toggle);
        var left = new StackPanel();
        if (item.Risk is { } level)
        {
            left.Children.Add(CardKit.RiskBadge(level));
        }
        var stateLine = item.IsOn && _applied.TryGetValue(item.Title, out var when)
            ? $"Appliqué le {when.ToLocalTime():dd/MM/yyyy}"
            : item.State;
        if (stateLine is not null)
        {
            left.Children.Add(new TextBlock { Text = stateLine, FontSize = 11.5, Foreground = CardKit.Muted, Margin = new Thickness(1, item.Risk is null ? 0 : 6, 0, 0) });
        }
        bottom.Children.Add(left);

        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = item.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        head.Children.Add(chips);
        head.Children.Add(new TextBlock { Text = item.Description, Foreground = CardKit.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxHeight = 52, TextTrimming = TextTrimming.CharacterEllipsis });

        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        panel.Children.Add(head);

        toggle.Click += async (_, _) => await ToggleAsync(item, toggle);
        return CardKit.Card(panel);
    }

    /// <summary>Avertissement avant un réglage modéré ou risqué ; vrai si l'utilisateur accepte.</summary>
    private bool Confirm(Item item)
    {
        if (item.Risk is null or TweakRisk.Safe)
        {
            return true;
        }
        var heading = item.Risk == TweakRisk.Risky ? "Réglage risqué" : "Réglage avec conséquences";
        return ConfirmDialog.Ask(Window.GetWindow(this), heading,
            $"{item.Title}\n\nCe qui peut arriver : {item.Consequence}\n\nLa valeur d’origine est sauvegardée : vous pourrez revenir en arrière à tout moment (bouton « Tout restaurer » ou en désactivant ce réglage).",
            [], "Appliquer", danger: item.Risk == TweakRisk.Risky);
    }

    private async Task ToggleAsync(Item item, CheckBox toggle)
    {
        var target = toggle.IsChecked == true;
        if (target && !Confirm(item))
        {
            toggle.IsChecked = !target;
            return;
        }
        toggle.IsEnabled = false;
        string? error;
        try
        {
            error = await item.Apply(target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException or ScanEngineException)
        {
            error = exception.Message;
        }
        App.History.Add(HistoryCategory.Optimization, target ? "Réglage appliqué" : "Réglage restauré", item.Title, error ?? (target ? "activé" : "désactivé"), error is null);
        toggle.IsEnabled = true;
        if (error is not null)
        {
            toggle.IsChecked = !target;
            _status.Text = $"{item.Title} : {error}";
            return;
        }
        item.IsOn = target;
        _status.Text = $"{item.Title} : {(target ? "appliqué" : "restauré")}.";
        if (item.RefreshAfter || _hideApplied)
        {
            await RefreshAsync();
        }
        else if (target)
        {
            _applied[item.Title] = DateTimeOffset.Now;
            Render();
        }
    }

    private async Task RestoreAllAsync()
    {
        if (_backup.Values.Count == 0)
        {
            _status.Text = "Rien à restaurer : aucun réglage n’a été modifié par OPTIVANCE.";
            return;
        }
        if (!ConfirmDialog.Ask(Window.GetWindow(this), "Tout restaurer ?", "Tous les réglages modifiés par OPTIVANCE reviennent à leur valeur d’origine.", [], "Restaurer", danger: false))
        {
            return;
        }
        var failures = await OptimizationRestore.RestoreAllAsync(_backup, Session);
        App.History.Add(HistoryCategory.Optimization, "Tout restaurer", "Optimisation", failures == 0 ? "état d’origine rétabli" : $"{failures} réglage(s) non restauré(s)", failures == 0);
        await RefreshAsync();
        if (failures > 0)
        {
            _status.Text = $"{failures} réglage(s) n’ont pas pu être restaurés (autorisation administrateur requise ?).";
        }
    }
}
