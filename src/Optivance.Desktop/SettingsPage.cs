using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Optivance.Core;
using Optivance.Infrastructure;

namespace Optivance.Desktop;

/// <summary>Paramètres modifiables par l'utilisateur ; chaque changement est enregistré aussitôt et tracé.</summary>
public sealed class SettingsPage : UserControl
{
    private readonly MainWindow _owner;
    private readonly StackPanel _content = new() { Margin = new Thickness(32, 26, 32, 32), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

    public SettingsPage(MainWindow owner)
    {
        _owner = owner;
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        Content = new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Build();
    }

    private static AppSettings S => App.Settings;

    private void Build()
    {
        _content.Children.Add(new TextBlock { Text = "Paramètres", FontSize = 22, FontWeight = FontWeights.SemiBold });
        _content.Children.Add(new TextBlock { Text = "Tout reste sur ce PC ; les changements sont appliqués et enregistrés immédiatement.", Foreground = Muted, Margin = new Thickness(1, 3, 0, 18) });

        var general = Section("Général");
        Toggle(general, "Demander l’autorisation administrateur au lancement", "Une seule fois par session ; permet l’analyse rapide (MFT), le nettoyage approfondi et les FPS du widget.", S.RequestElevationAtStartup, value => S.RequestElevationAtStartup = value);
        Toggle(general, "Fermer la fenêtre = réduire dans la zone de notification", "OPTIVANCE et le widget restent actifs près de l’horloge ; « Quitter » dans le menu de l’icône arrête tout.", S.CloseToTray, value => S.CloseToTray = value);
        Toggle(general, "Lancer OPTIVANCE avec Windows", "Démarre masqué dans la zone de notification (entrée du compte courant, retirée en décochant).", ShellIntegration.StartsWithWindows(), value =>
        {
            ShellIntegration.SetStartWithWindows(value);
            S.StartWithWindows = value;
            S.StartHiddenInTray = value;
        });

        var storage = Section("Stockage");
        Toggle(storage, "Utiliser l’analyse rapide (MFT) quand c’est possible", "Disques NTFS entiers, si l’autorisation administrateur a été accordée.", S.PreferFastScan, value => S.PreferFastScan = value);
        Toggle(storage, "Confirmer avant de mettre à la Corbeille", "La suppression définitive (Maj+Suppr) est toujours confirmée.", S.ConfirmDeletion, value => S.ConfirmDeletion = value);
        Toggle(storage, "« OPTIVANCE » dans le clic droit du Bureau", "Entrée du compte courant ; sous Windows 11, aussi dans « Afficher plus d’options ».", ShellIntegration.IsRegistered(), value =>
        {
            if (value)
            {
                ShellIntegration.Register();
            }
            else
            {
                ShellIntegration.Unregister();
            }
        });

        var monitoring = Section("Monitoring");
        Choice(monitoring, "Fréquence de mise à jour", "Une fréquence plus basse consomme encore moins.",
            [("0,5 s", 500), ("1 s", 1000), ("2 s", 2000), ("5 s", 5000)], S.MonitoringRefreshMilliseconds, value => S.MonitoringRefreshMilliseconds = value);

        var widget = Section("Widget");
        Slider(widget, "Opacité du widget", S.WidgetOpacity, value => S.WidgetOpacity = value);
        Toggle(widget, "Laisser passer les clics à travers le widget", "Le widget ne capte plus la souris (utile en jeu).", S.WidgetClickThrough, value => S.WidgetClickThrough = value);
        Hotkey(widget, "Raccourci : afficher / masquer le widget", S.WidgetToggleHotkey, value => S.WidgetToggleHotkey = value);
        Hotkey(widget, "Raccourci : clics à travers le widget", S.WidgetClickThroughHotkey, value => S.WidgetClickThroughHotkey = value);
        Toggle(widget, "Afficher le processeur", null, S.WidgetShowCpu, value => S.WidgetShowCpu = value);
        Toggle(widget, "Afficher la carte graphique", null, S.WidgetShowGpu, value => S.WidgetShowGpu = value);
        Toggle(widget, "Afficher la mémoire", null, S.WidgetShowMemory, value => S.WidgetShowMemory = value);
        Toggle(widget, "Afficher les températures", null, S.WidgetShowTemperatures, value => S.WidgetShowTemperatures = value);
        Toggle(widget, "Afficher les FPS", "Lus par les événements d’affichage de Windows, sans toucher au jeu (autorisation administrateur requise).", S.WidgetShowFps, value => S.WidgetShowFps = value);
        Toggle(widget, "Afficher le réseau", null, S.WidgetShowNetwork, value => S.WidgetShowNetwork = value);
        Toggle(widget, "Afficher la consommation de la carte graphique", null, S.WidgetShowGpuPower, value => S.WidgetShowGpuPower = value);
        Toggle(widget, "Afficher l’espace disque", "Espace libre du disque système.", S.WidgetShowDisk, value => S.WidgetShowDisk = value);
        Toggle(widget, "Widget réduit", "Une seule ligne, sans titre (aussi accessible par clic droit sur le widget).", S.WidgetCompact, value => S.WidgetCompact = value);

        var alerts = Section("Alertes");
        (string, int)[] temperatures = [.. Enumerable.Range(0, 13).Select(i => ($"{60 + i * 5} °C", 60 + i * 5))];
        Toggle(alerts, "Alerte de température du processeur", "Si Windows fournit la température (sans pilote).", S.AlertCpuTemperature, value => S.AlertCpuTemperature = value);
        Choice(alerts, "Seuil processeur", null, temperatures, S.AlertCpuCelsius, value => S.AlertCpuCelsius = value);
        Toggle(alerts, "Alerte de température de la carte graphique", null, S.AlertGpuTemperature, value => S.AlertGpuTemperature = value);
        Choice(alerts, "Seuil carte graphique", null, temperatures, S.AlertGpuCelsius, value => S.AlertGpuCelsius = value);
        Toggle(alerts, "Alerte d’espace disque faible", "Vérifiée une fois par minute sur tous les disques internes.", S.AlertDiskSpace, value => S.AlertDiskSpace = value);
        Choice(alerts, "Seuil d’espace libre", null, [("5 %", 5), ("10 %", 10), ("15 %", 15), ("20 %", 20), ("25 %", 25)], S.AlertDiskFreePercent, value => S.AlertDiskFreePercent = value);
        Toggle(alerts, "Notification Windows", "Une notification par dépassement, pas de répétition.", S.AlertNotification, value => S.AlertNotification = value);
        Toggle(alerts, "Son", null, S.AlertSound, value => S.AlertSound = value);

        var history = Section("Historique");
        Choice(history, "Conserver l’historique", null, [("30 jours", 30), ("90 jours", 90), ("6 mois", 180), ("1 an", 365)], S.HistoryRetentionDays, value => S.HistoryRetentionDays = value);
        Action(history, "Effacer tout l’historique des actions", "Effacer", () =>
        {
            if (Storage.ConfirmDialog.Ask(Window.GetWindow(this), "Effacer l’historique ?", "Toutes les actions enregistrées seront supprimées. Cette opération est irréversible.", [], "Effacer", danger: true))
            {
                App.History.Clear();
            }
        });
        Action(history, "Dossier des données d’OPTIVANCE (paramètres, historique, journaux)", "Ouvrir", () =>
        {
            Directory.CreateDirectory(AppSettings.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Folder}\"") { UseShellExecute = true })?.Dispose();
        });
    }

    private static Brush Muted => (Brush)Application.Current.FindResource("MutedTextBrush");

    private StackPanel Section(string title)
    {
        var panel = new StackPanel();
        _content.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Style = (Style)Application.Current.FindResource("Caption"), Margin = new Thickness(2, 10, 0, 8) });
        _content.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 0, 14), Child = panel });
        return panel;
    }

    private static Grid Row(StackPanel section, string label, string? hint, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = label, FontSize = 13 });
        if (hint is not null)
        {
            text.Children.Add(new TextBlock { Text = hint, Foreground = Muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        }
        grid.Children.Add(text);
        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (section.Children.Count > 0)
        {
            section.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x36)) });
        }
        section.Children.Add(grid);
        return grid;
    }

    private void Toggle(StackPanel section, string label, string? hint, bool value, Action<bool> apply)
    {
        var box = new CheckBox { IsChecked = value, Style = (Style)Application.Current.FindResource("Switch") };
        box.Click += (_, _) => Apply(label, box.IsChecked == true ? "activé" : "désactivé", () => apply(box.IsChecked == true), () => box.IsChecked = !box.IsChecked);
        Row(section, label, hint, box);
    }

    private void Choice(StackPanel section, string label, string? hint, (string Text, int Value)[] options, int current, Action<int> apply)
    {
        var combo = new ComboBox { MinWidth = 150, ItemContainerStyle = (Style)Application.Current.FindResource("DarkComboBoxItem") };
        foreach (var option in options)
        {
            combo.Items.Add(option.Text);
        }
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(options, option => option.Value == current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
            {
                Apply(label, options[combo.SelectedIndex].Text, () => apply(options[combo.SelectedIndex].Value), null);
            }
        };
        Row(section, label, hint, combo);
    }

    private void Slider(StackPanel section, string label, double value, Action<double> apply)
    {
        var slider = new Slider { Minimum = 0.25, Maximum = 1, Value = value, Width = 180, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        var shown = new TextBlock { Text = $"{value:P0}", Width = 44, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, args) => shown.Text = $"{args.NewValue:P0}";
        slider.LostMouseCapture += (_, _) => Apply(label, shown.Text, () => apply(slider.Value), null);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(shown);
        Row(section, label, null, panel);
    }

    /// <summary>Champ de raccourci : on clique puis on presse la combinaison voulue (Échap annule).</summary>
    private void Hotkey(StackPanel section, string label, string value, Action<string> apply)
    {
        var box = new TextBox { Text = value, Width = 170, IsReadOnly = true, Cursor = Cursors.Hand, ToolTip = "Cliquez puis pressez la combinaison (ex. Ctrl+Alt+W)" };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                Keyboard.ClearFocus();
                return;
            }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                return;
            }
            var modifiers = Keyboard.Modifiers;
            if (modifiers == ModifierKeys.None)
            {
                box.Text = "Ajoutez Ctrl, Alt ou Maj";
                return;
            }
            var text = HotkeyText.Format(modifiers, key);
            box.Text = text;
            Apply(label, text, () => apply(text), null);
            Keyboard.ClearFocus();
        };
        Row(section, label, null, box);
    }

    private void Action(StackPanel section, string label, string button, Action action)
    {
        var control = new Button { Content = button, MinWidth = 100, Padding = new Thickness(14, 6, 14, 6) };
        control.Click += (_, _) => action();
        Row(section, label, null, control);
    }

    private void Apply(string label, string value, Action apply, Action? revert)
    {
        try
        {
            apply();
            S.Save();
            App.History.Add(HistoryCategory.Settings, "Paramètre modifié", label, value);
            SettingsChanged?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            revert?.Invoke();
            Storage.ConfirmDialog.Inform(Window.GetWindow(this), "Paramètre non appliqué", exception.Message);
        }
    }

    public static event Action? SettingsChanged;
}

/// <summary>Texte d'un raccourci (« Ctrl+Alt+W ») et analyse inverse.</summary>
public static class HotkeyText
{
    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Maj");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "maj" or "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key))
                    {
                        return false;
                    }
                    break;
            }
        }
        return key != Key.None && modifiers != ModifierKeys.None;
    }
}
