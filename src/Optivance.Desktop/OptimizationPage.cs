using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Optivance.Core;
using Optivance.Desktop.Storage;
using Optivance.Infrastructure;
using Optivance.Infrastructure.Optimization;

namespace Optivance.Desktop;

/// <summary>
/// Optimisation : alimentation, réglages du compte, applications au démarrage, services et
/// tâches planifiées. Chaque modification est précédée d'un avertissement si elle comporte un
/// risque, sauvegardée (valeur d'origine) et inscrite dans l'historique ; tout est restaurable.
/// </summary>
public sealed class OptimizationPage : UserControl
{
    private readonly MainWindow _owner;
    private readonly OptimizationBackup _backup = new();
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Brush _muted;
    private bool _refreshing;

    public OptimizationPage(MainWindow owner)
    {
        _owner = owner;
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        _muted = (Brush)Application.Current.FindResource("MutedTextBrush");
        _status.Foreground = _muted;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Optimisation", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Chaque réglage est sauvegardé avant modification et peut être restauré. Les réglages risqués affichent leurs conséquences avant d’être appliqués.", Foreground = _muted, Margin = new Thickness(1, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        var restoreAll = new Button { Content = "Tout restaurer", Style = (Style)Application.Current.FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 130 };
        restoreAll.Click += async (_, _) => await RestoreAllAsync();
        DockPanel.SetDock(restoreAll, Dock.Right);
        header.Children.Add(restoreAll);
        header.Children.Add(titles);

        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 20) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        _status.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(header);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) });
        Content = root;

        Loaded += async (_, _) => await RefreshAsync();
        _owner.Storage.SessionChanged += () => Dispatcher.BeginInvoke(async () => await RefreshAsync());
    }

    private MftSession? Session => _owner.Storage.Session;

    // ───────────────────────────── Construction ─────────────────────────────

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

        _body.Children.Clear();
        _body.Children.Add(PowerSection(plans));
        _body.Children.Add(UserSection());
        _body.Children.Add(StartupSection(startup));
        _body.Children.Add(ServiceSection(services));
        _body.Children.Add(TaskSection(tasks));
        var modified = _backup.Values.Count;
        _status.Text = modified == 0 ? "Aucun réglage modifié par OPTIVANCE pour l’instant." : $"{modified} valeur(s) d’origine sauvegardée(s) : « Tout restaurer » revient à l’état d’avant OPTIVANCE.";
        _refreshing = false;
    }

    private Border Card(string title, string subtitle, out StackPanel content, UIElement? notice = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = _muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) });
        if (notice is not null)
        {
            panel.Children.Add(notice);
        }
        content = panel;
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(18, 16, 18, 8), Margin = new Thickness(0, 0, 0, 16), Child = panel };
    }

    private UIElement AdminNotice()
    {
        if (Session is not null)
        {
            return new Border();
        }
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        line.Children.Add(new TextBlock { Text = "Autorisation administrateur non accordée pour cette session : lecture seule.", Foreground = _muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        if (MftScanClient.IsHelperPresent)
        {
            var authorize = new Button { Content = "Autoriser", Style = (Style)Application.Current.FindResource("GhostButton") };
            authorize.Click += async (_, _) => await _owner.Storage.InitializeSessionAsync();
            line.Children.Add(authorize);
        }
        return line;
    }

    private static readonly Brush SafeBrush = new SolidColorBrush(Color.FromRgb(0x72, 0xE0, 0xB1));
    private static readonly Brush ModerateBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x4B));
    private static readonly Brush RiskyBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0x6D, 0x6D));

    private static Border RiskBadge(TweakRisk risk)
    {
        var (text, brush) = risk switch
        {
            TweakRisk.Safe => ("Sans risque", SafeBrush),
            TweakRisk.Moderate => ("Modéré", ModerateBrush),
            _ => ("Risqué", RiskyBrush)
        };
        return new Border
        {
            BorderBrush = brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2),
            Margin = new Thickness(10, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = text, Foreground = brush, FontSize = 11 }
        };
    }

    /// <summary>Ligne : titre (+ badge), description, élément à droite (interrupteur, état).</summary>
    private void Row(StackPanel panel, string title, string description, TweakRisk? risk, UIElement right, string? state = null)
    {
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        titleLine.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (risk is { } level)
        {
            titleLine.Children.Add(RiskBadge(level));
        }
        var texts = new StackPanel();
        texts.Children.Add(titleLine);
        texts.Children.Add(new TextBlock { Text = description, Foreground = _muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (state is not null)
        {
            rightPanel.Children.Add(new TextBlock { Text = state, Foreground = _muted, FontSize = 12, Margin = new Thickness(0, 2, 12, 0), MinWidth = 120, TextAlignment = TextAlignment.Right });
        }
        rightPanel.Children.Add(right);
        DockPanel.SetDock(rightPanel, Dock.Right);
        line.Children.Add(rightPanel);
        line.Children.Add(texts);
        panel.Children.Add(line);
    }

    private static CheckBox Switch(bool value, bool enabled = true) =>
        new() { IsChecked = value, IsEnabled = enabled, Style = (Style)Application.Current.FindResource("Switch"), VerticalAlignment = VerticalAlignment.Top };

    /// <summary>Avertissement avant un réglage modéré ou risqué ; vrai si l'utilisateur accepte.</summary>
    private bool Confirm(string title, TweakRisk risk, string consequence)
    {
        if (risk == TweakRisk.Safe)
        {
            return true;
        }
        var heading = risk == TweakRisk.Risky ? "Réglage risqué" : "Réglage avec conséquences";
        return ConfirmDialog.Ask(Window.GetWindow(this), heading,
            $"{title}\n\nCe qui peut arriver : {consequence}\n\nLa valeur d’origine est sauvegardée : vous pourrez revenir en arrière à tout moment (bouton « Tout restaurer » ou en désactivant ce réglage).",
            [], "Appliquer", danger: risk == TweakRisk.Risky);
    }

    /// <summary>Branche un interrupteur : confirmation, action, historique ; état remis si refus ou échec.</summary>
    private void Wire(CheckBox box, string title, TweakRisk risk, string consequence, Func<bool, Task<string?>> apply)
    {
        box.Click += async (_, _) =>
        {
            var target = box.IsChecked == true;
            if (target && !Confirm(title, risk, consequence))
            {
                box.IsChecked = !target;
                return;
            }
            box.IsEnabled = false;
            string? error;
            try
            {
                error = await apply(target);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException or ScanEngineException)
            {
                error = exception.Message;
            }
            App.History.Add(HistoryCategory.Optimization, target ? "Réglage appliqué" : "Réglage restauré", title, error ?? (target ? "activé" : "désactivé"), error is null);
            if (error is not null)
            {
                box.IsChecked = !target;
                _status.Text = $"{title} : {error}";
            }
            else
            {
                _status.Text = $"{title} : {(target ? "appliqué" : "restauré")}.";
            }
            box.IsEnabled = true;
        };
    }

    private async Task<string?> RunTune(string command)
    {
        if (Session is not { } session)
        {
            return "autorisation administrateur requise.";
        }
        var code = await session.TuneAsync(command, CancellationToken.None);
        return code == 0 ? null : code == -2 ? "commande refusée par le composant administrateur." : $"Windows a refusé la modification (code {code}).";
    }

    // ───────────────────────────── Sections ─────────────────────────────

    private Border PowerSection(IReadOnlyList<PowerPlan> plans)
    {
        var card = Card("Mode d’alimentation", "« Performances élevées » maximise la réactivité ; sur un portable, il augmente la consommation et la chaleur.", out var panel);
        var choices = new WrapPanel();
        foreach (var plan in plans)
        {
            var button = new RadioButton { Content = plan.Name, IsChecked = plan.Active, GroupName = "power", Margin = new Thickness(0, 0, 22, 8), Foreground = (Brush)Application.Current.FindResource("TextBrush") };
            button.Checked += (_, _) =>
            {
                if (_refreshing)
                {
                    return;
                }
                if (PowerPlans.Active() is { } previous)
                {
                    _backup.Remember("power", previous.ToString());
                }
                var ok = PowerPlans.SetActive(plan.Id);
                App.History.Add(HistoryCategory.Optimization, "Mode d’alimentation", plan.Name, ok ? "activé" : "refusé par Windows", ok);
                _status.Text = ok ? $"Mode d’alimentation : {plan.Name}." : "Windows a refusé le changement de mode d’alimentation.";
            };
            choices.Children.Add(button);
        }
        if (plans.Count == 0)
        {
            choices.Children.Add(new TextBlock { Text = "Modes d’alimentation indisponibles (gérés par le fabricant ou Windows en mode « Moderne »).", Foreground = _muted });
        }
        panel.Children.Add(choices);
        return card;
    }

    private Border UserSection()
    {
        var card = Card("Réglages de Windows", "Réglages de votre compte, appliqués immédiatement.", out var panel);
        foreach (var tweak in UserTweaks.All)
        {
            var box = Switch(UserTweaks.IsOptimized(tweak));
            Wire(box, tweak.Title, tweak.Risk, tweak.Consequence, enabled =>
            {
                if (enabled)
                {
                    UserTweaks.Apply(tweak, _backup);
                }
                else
                {
                    UserTweaks.Restore(tweak, _backup);
                }
                return Task.FromResult<string?>(null);
            });
            Row(panel, tweak.Title, tweak.Description, tweak.Risk, box);
        }
        return card;
    }

    private Border StartupSection(IReadOnlyList<StartupApp> apps)
    {
        var card = Card("Applications au démarrage", "Désactiver une application l’empêche de se lancer avec Windows, sans la désinstaller (même mécanisme que le Gestionnaire des tâches).", out var panel, AdminNotice());
        if (apps.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "Aucune application au démarrage.", Foreground = _muted, Margin = new Thickness(0, 0, 0, 10) });
        }
        foreach (var app in apps)
        {
            var canToggle = app.CanToggle && (!app.RequiresAdmin || (Session is not null && StartupApps.CanSendToHelper(app)));
            var box = Switch(app.Enabled, canToggle);
            box.ToolTip = canToggle ? null : app.Location == StartupLocation.CommonFolder ? "Dossier Démarrage commun : à modifier depuis l’Explorateur." : "Autorisation administrateur requise.";
            var key = $"startup:{app.Location}|{app.Name}";
            Wire(box, $"Démarrage : {app.Name}", TweakRisk.Safe, "", async enabled =>
            {
                _backup.Remember(key, app.Enabled ? "true" : "false");
                if (app.RequiresAdmin)
                {
                    return await RunTune(StartupApps.MachineCommand(app, enabled));
                }
                StartupApps.SetUserEnabled(app, enabled);
                return null;
            });
            Row(panel, app.Name, app.Command, null, box, app.LocationLabel);
        }
        return card;
    }

    private Border ServiceSection(IReadOnlyList<(ServiceTweak Service, string Mode)> services)
    {
        var card = Card("Services Windows", "Interrupteur activé = service désactivé. L’état d’origine est restauré en désactivant l’interrupteur.", out var panel, AdminNotice());
        foreach (var (service, mode) in services.Where(s => s.Mode != "missing"))
        {
            var key = $"svc:{service.Name}";
            var box = Switch(mode == "disabled", Session is not null);
            Wire(box, $"Service : {service.Title}", service.Risk, service.Consequence, async disable =>
            {
                if (disable)
                {
                    _backup.Remember(key, SystemState.ServiceMode(service.Name));
                    return await RunTune($"{{\"v\":3,\"type\":\"tune-service\",\"name\":\"{service.Name}\",\"start\":\"disabled\"}}");
                }
                var original = _backup.TryGet(key, out var saved) && saved != "disabled" && saved != "unknown" ? saved : "demand";
                var error = await RunTune($"{{\"v\":3,\"type\":\"tune-service\",\"name\":\"{service.Name}\",\"start\":\"{original}\"}}");
                if (error is null)
                {
                    _backup.Forget(key);
                }
                return error;
            });
            Row(panel, service.Title, service.Description, service.Risk, box, OptimizationCatalog.StartModeLabel(mode));
        }
        return card;
    }

    private Border TaskSection(IReadOnlyList<(TaskTweak Task, bool? Enabled)> tasks)
    {
        var card = Card("Tâches planifiées", "Tâches de collecte de données de Windows. Interrupteur activé = tâche désactivée.", out var panel, AdminNotice());
        foreach (var (task, enabled) in tasks.Where(t => t.Enabled is not null || Session is not null))
        {
            var key = $"task:{task.Id}";
            var box = Switch(enabled == false, Session is not null);
            Wire(box, $"Tâche : {task.Title}", task.Risk, task.Consequence, async disable =>
            {
                if (disable)
                {
                    _backup.Remember(key, enabled == false ? "false" : "true");
                }
                var reenable = !disable && (!_backup.TryGet(key, out var saved) || saved == "true");
                var error = await RunTune($"{{\"v\":3,\"type\":\"tune-task\",\"id\":\"{task.Id}\",\"enabled\":\"{(reenable ? "true" : "false")}\"}}");
                if (error is null && !disable)
                {
                    _backup.Forget(key);
                }
                return error;
            });
            Row(panel, task.Title, task.Path, task.Risk, box, enabled switch { true => "Active", false => "Désactivée", null => "État inconnu" });
        }
        return card;
    }

    // ───────────────────────────── Tout restaurer ─────────────────────────────

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
        var failures = 0;
        foreach (var tweak in UserTweaks.All)
        {
            UserTweaks.Restore(tweak, _backup);
        }
        if (_backup.TryGet("power", out var plan) && Guid.TryParse(plan, out var id))
        {
            if (PowerPlans.SetActive(id)) { _backup.Forget("power"); } else { failures++; }
        }
        foreach (var (key, value) in _backup.Values.ToList())
        {
            string? error = null;
            if (key.StartsWith("svc:"))
            {
                var name = key[4..];
                var mode = value is "disabled" or "unknown" ? "demand" : value;
                error = await RunTune($"{{\"v\":3,\"type\":\"tune-service\",\"name\":\"{name}\",\"start\":\"{mode}\"}}");
            }
            else if (key.StartsWith("task:"))
            {
                error = await RunTune($"{{\"v\":3,\"type\":\"tune-task\",\"id\":\"{key[5..]}\",\"enabled\":\"{value}\"}}");
            }
            else if (key.StartsWith("startup:"))
            {
                var app = StartupApps.List().FirstOrDefault(a => key == $"startup:{a.Location}|{a.Name}");
                if (app is not null)
                {
                    var enabled = value == "true";
                    if (app.RequiresAdmin) { error = await RunTune(StartupApps.MachineCommand(app, enabled)); }
                    else { StartupApps.SetUserEnabled(app, enabled); }
                }
            }
            else
            {
                continue;
            }
            if (error is null) { _backup.Forget(key); } else { failures++; }
        }
        App.History.Add(HistoryCategory.Optimization, "Tout restaurer", "Optimisation", failures == 0 ? "état d’origine rétabli" : $"{failures} réglage(s) non restauré(s)", failures == 0);
        await RefreshAsync();
        if (failures > 0)
        {
            _status.Text = $"{failures} réglage(s) n’ont pas pu être restaurés (autorisation administrateur requise ?).";
        }
    }
}
