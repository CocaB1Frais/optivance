using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Optivance.Core;
using Optivance.Core.Storage;
using Optivance.Infrastructure.Monitoring;

namespace Optivance.Desktop.Monitoring;

/// <summary>
/// Widget transparent, toujours au premier plan, déplaçable, hors de la barre des tâches et
/// d'Alt+Tab. Dans le même processus qu'OPTIVANCE (D-17) : il reste affiché quand la fenêtre
/// principale est fermée dans la zone de notification. Mesures relevées seulement s'il est visible.
/// </summary>
public sealed class WidgetWindow : Window
{
    private readonly StackPanel _rows = new();
    private readonly Dictionary<string, TextBlock> _values = [];
    private IDisposable? _subscription;
    private readonly System.Windows.Threading.DispatcherTimer _keepOnTop = new() { Interval = TimeSpan.FromSeconds(2) };

    public WidgetWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;
        Title = "OPTIVANCE — widget";
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 14, 17, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 114, 224, 177)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Child = _rows,
            ToolTip = "Glisser pour déplacer · clic droit : indicateurs, réduire, alertes"
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                App.Settings.WidgetLeft = Left;
                App.Settings.WidgetTop = Top;
                App.Settings.Save();
            }
        };
        SourceInitialized += (_, _) => ApplyWindowStyles();
        // Un jeu (plein écran fenêtré) ou une autre fenêtre « au premier plan » peut passer devant :
        // le widget se replace au-dessus toutes les 2 s (sans prendre le focus, coût négligeable).
        _keepOnTop.Tick += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
            }
        };
        if (AlertService.Current is { } alerts)
        {
            alerts.Changed += () =>
            {
                _lastDisk = DateTime.MinValue;
                if (IsVisible && SystemMonitor.Shared.Last is { } last)
                {
                    Show(last);
                }
            };
        }
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _keepOnTop.Start();
                SystemMonitor.Shared.IntervalMilliseconds = Math.Max(1000, App.Settings.MonitoringRefreshMilliseconds);
                _subscription ??= SystemMonitor.Shared.Subscribe(snapshot => Dispatcher.BeginInvoke(() => Show(snapshot)));
            }
            else
            {
                _keepOnTop.Stop();
                _subscription?.Dispose();
                _subscription = null;
            }
        };
        ApplySettings();
    }

    /// <summary>Contenu, opacité, position et clics à travers selon les paramètres.</summary>
    public void ApplySettings()
    {
        var settings = App.Settings;
        Opacity = settings.WidgetOpacity;
        _rows.Children.Clear();
        _values.Clear();
        _compact = settings.WidgetCompact;
        _rows.Orientation = _compact ? Orientation.Horizontal : Orientation.Vertical;
        if (!_compact)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/icon.png")), Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0) });
            header.Children.Add(new TextBlock { Text = "OPTIVANCE", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x99, 0xAC)) });
            _rows.Children.Add(header);
        }
        if (settings.WidgetShowFps) Row("fps", "FPS", Color.FromRgb(0x72, 0xE0, 0xB1));
        if (settings.WidgetShowCpu) Row("cpu", "CPU", Color.FromRgb(0x4F, 0x8D, 0xF5));
        if (settings.WidgetShowGpu) Row("gpu", "GPU", Color.FromRgb(0x3C, 0xC4, 0x8A));
        if (settings.WidgetShowTemperatures) Row("temp", "Temp.", Color.FromRgb(0xF2, 0xB3, 0x3D));
        if (settings.WidgetShowGpuPower) Row("power", "Conso GPU", Color.FromRgb(0xE8, 0x61, 0x5A));
        if (settings.WidgetShowMemory) Row("ram", "RAM", Color.FromRgb(0x9B, 0x6C, 0xF0));
        if (settings.WidgetShowNetwork) Row("net", "Réseau", Color.FromRgb(0x2F, 0xC4, 0xD6));
        if (settings.WidgetShowDisk) Row("disk", "Disque", Color.FromRgb(0xC9, 0xD3, 0xE0));
        if (_values.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = "Aucun indicateur : clic droit pour en choisir", FontSize = 11, Foreground = Brushes.White });
        }
        if (double.IsFinite(settings.WidgetLeft) && double.IsFinite(settings.WidgetTop))
        {
            Left = settings.WidgetLeft;
            Top = settings.WidgetTop;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - 220;
            Top = SystemParameters.WorkArea.Top + 40;
        }
        ContextMenu = BuildMenu();
        ApplyWindowStyles();
        _lastDisk = DateTime.MinValue;
        if (SystemMonitor.Shared.Last is { } last)
        {
            Show(last);
        }
    }

    private bool _compact;
    private DateTime _lastDisk = DateTime.MinValue;
    private static readonly Brush AlertBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x5E));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x4B));

    /// <summary>Clic droit : indicateurs, mode réduit, clics à travers, masquer.</summary>
    private ContextMenu BuildMenu()
    {
        var s = App.Settings;
        var menu = new ContextMenu();
        MenuItem Check(string header, bool value, Action<bool> apply)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = value, StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                apply(item.IsChecked);
                App.Settings.Save();
                Dispatcher.BeginInvoke(ApplySettings);
            };
            return item;
        }
        var indicators = new MenuItem { Header = "Indicateurs" };
        indicators.Items.Add(Check("FPS", s.WidgetShowFps, v => s.WidgetShowFps = v));
        indicators.Items.Add(Check("Processeur", s.WidgetShowCpu, v => s.WidgetShowCpu = v));
        indicators.Items.Add(Check("Carte graphique", s.WidgetShowGpu, v => s.WidgetShowGpu = v));
        indicators.Items.Add(Check("Températures", s.WidgetShowTemperatures, v => s.WidgetShowTemperatures = v));
        indicators.Items.Add(Check("Consommation GPU", s.WidgetShowGpuPower, v => s.WidgetShowGpuPower = v));
        indicators.Items.Add(Check("Mémoire", s.WidgetShowMemory, v => s.WidgetShowMemory = v));
        indicators.Items.Add(Check("Réseau", s.WidgetShowNetwork, v => s.WidgetShowNetwork = v));
        indicators.Items.Add(Check("Espace disque", s.WidgetShowDisk, v => s.WidgetShowDisk = v));
        menu.Items.Add(indicators);
        var compact = new MenuItem { Header = s.WidgetCompact ? "Agrandir le widget" : "Réduire le widget" };
        compact.Click += (_, _) => { s.WidgetCompact = !s.WidgetCompact; s.Save(); ApplySettings(); };
        menu.Items.Add(compact);
        var through = new MenuItem { Header = "Laisser passer les clics (" + s.WidgetClickThroughHotkey + " pour revenir)" };
        through.Click += (_, _) => { s.WidgetClickThrough = true; s.Save(); ApplyWindowStyles(); };
        menu.Items.Add(through);
        var reset = new MenuItem { Header = "Remettre en haut à droite" };
        reset.Click += (_, _) => { s.WidgetLeft = double.NaN; s.WidgetTop = double.NaN; s.Save(); ApplySettings(); };
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        var settingsItem = new MenuItem { Header = "Alertes et réglages…" };
        settingsItem.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.OpenPage("Settings");
        menu.Items.Add(settingsItem);
        var hide = new MenuItem { Header = "Masquer le widget (" + s.WidgetToggleHotkey + ")" };
        hide.Click += (_, _) => (Application.Current.MainWindow as MainWindow)?.ToggleWidget();
        menu.Items.Add(hide);
        return menu;
    }

    private void Row(string key, string label, Color color)
    {
        var value = new TextBlock { Text = "…", FontSize = _compact ? 12 : 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(_compact ? 5 : 14, 0, 0, 0) };
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(new TextBlock { Text = label, FontSize = _compact ? 11 : 12, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD1, 0xDE)) });
        if (_compact)
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(name);
            cell.Children.Add(value);
            _rows.Children.Add(cell);
        }
        else
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), MinWidth = 170 };
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(name);
            _rows.Children.Add(row);
        }
        _values[key] = value;
    }

    private void Set(string key, string text, bool alert = false)
    {
        if (_values.TryGetValue(key, out var block))
        {
            block.Text = text;
            block.Foreground = alert ? AlertBrush : Brushes.White;
        }
    }

    private void Show(SystemSnapshot s)
    {
        var alerts = AlertService.Current;
        Set("fps", s.Fps is { } fps ? MonitorFormat.Fps(fps) : "—");
        Set("cpu", MonitorFormat.Percent(s.CpuPercent));
        Set("gpu", MonitorFormat.Percent(s.GpuPercent));
        var temperatures = new List<string>();
        if (s.ThermalCelsius is { } cpu) temperatures.Add($"CPU {cpu:0}°");
        if (s.GpuCelsius is { } gpu) temperatures.Add($"GPU {gpu:0}°");
        Set("temp", temperatures.Count == 0 ? "—" : string.Join("  ", temperatures),
            alerts is not null && (alerts.IsActive(AlertService.CpuKey) || alerts.IsActive(AlertService.GpuKey)));
        Set("power", MonitorFormat.Watts(s.GpuWatts));
        Set("ram", _compact ? MonitorFormat.Percent(s.MemoryPercent) : $"{MonitorFormat.Percent(s.MemoryPercent)} · {StorageFormat.Bytes(s.MemoryUsed)}");
        Set("net", $"↓ {MonitorFormat.Rate(s.NetworkDownBytesPerSecond)}  ↑ {MonitorFormat.Rate(s.NetworkUpBytesPerSecond)}");
        if (_values.ContainsKey("disk") && DateTime.UtcNow - _lastDisk > TimeSpan.FromSeconds(30))
        {
            _lastDisk = DateTime.UtcNow;
            var system = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            var drive = AlertService.FixedDrives().FirstOrDefault(d => string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase));
            var low = alerts?.LowDisks.Count > 0;
            var text = drive is null ? "—" : $"{drive.Name.TrimEnd('\\')} {StorageFormat.Bytes((ulong)drive.AvailableFreeSpace)} libres";
            if (low == true)
            {
                text += $" · faible : {string.Join(", ", alerts!.LowDisks.Keys)}";
            }
            Set("disk", text, low == true);
        }
    }
    /// <summary>Hors d'Alt+Tab ; clics à travers si demandé (le widget ne capte plus la souris).</summary>
    private void ApplyWindowStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var style = GetWindowLongPtrW(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        style = App.Settings.WidgetClickThrough ? style | WsExTransparent | WsExLayered : style & ~WsExTransparent;
        SetWindowLongPtrW(handle, GwlExStyle, new IntPtr(style));
    }

    protected override void OnClosed(EventArgs e)
    {
        _subscription?.Dispose();
        base.OnClosed(e);
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);
}

/// <summary>Raccourcis clavier globaux (RegisterHotKey) ; actifs même fenêtre principale cachée.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly IntPtr _window;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 0xB000;

    public GlobalHotkeys(Window owner)
    {
        _window = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_window);
        _source.AddHook(Hook);
    }

    /// <summary>Remplace tous les raccourcis ; renvoie les combinaisons refusées (déjà prises par une autre application).</summary>
    public List<string> Register(IEnumerable<(string Text, Action Action)> hotkeys)
    {
        Clear();
        var refused = new List<string>();
        foreach (var (text, action) in hotkeys)
        {
            if (!HotkeyText.TryParse(text, out var modifiers, out var key))
            {
                refused.Add(text);
                continue;
            }
            var id = _nextId++;
            uint native = 0x4000; // MOD_NOREPEAT
            if (modifiers.HasFlag(ModifierKeys.Alt)) native |= 0x1;
            if (modifiers.HasFlag(ModifierKeys.Control)) native |= 0x2;
            if (modifiers.HasFlag(ModifierKeys.Shift)) native |= 0x4;
            if (modifiers.HasFlag(ModifierKeys.Windows)) native |= 0x8;
            if (RegisterHotKey(_window, id, native, (uint)KeyInterop.VirtualKeyFromKey(key)))
            {
                _actions[id] = action;
            }
            else
            {
                refused.Add(text);
            }
        }
        return refused;
    }

    private void Clear()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_window, id);
        }
        _actions.Clear();
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(Hook);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
