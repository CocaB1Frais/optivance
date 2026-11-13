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
            ToolTip = "Glisser pour déplacer · réglages dans Paramètres › Widget"
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
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                SystemMonitor.Shared.IntervalMilliseconds = Math.Max(1000, App.Settings.MonitoringRefreshMilliseconds);
                _subscription ??= SystemMonitor.Shared.Subscribe(snapshot => Dispatcher.BeginInvoke(() => Show(snapshot)));
            }
            else
            {
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
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/icon.png")), Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0) });
        header.Children.Add(new TextBlock { Text = "OPTIVANCE", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x99, 0xAC)) });
        _rows.Children.Add(header);
        if (settings.WidgetShowFps) Row("fps", "FPS", Color.FromRgb(0x72, 0xE0, 0xB1));
        if (settings.WidgetShowCpu) Row("cpu", "CPU", Color.FromRgb(0x4F, 0x8D, 0xF5));
        if (settings.WidgetShowGpu) Row("gpu", "GPU", Color.FromRgb(0x3C, 0xC4, 0x8A));
        if (settings.WidgetShowTemperatures) Row("temp", "Temp.", Color.FromRgb(0xF2, 0xB3, 0x3D));
        if (settings.WidgetShowGpu) Row("power", "Conso GPU", Color.FromRgb(0xE8, 0x61, 0x5A));
        if (settings.WidgetShowMemory) Row("ram", "RAM", Color.FromRgb(0x9B, 0x6C, 0xF0));
        if (settings.WidgetShowNetwork) Row("net", "Réseau", Color.FromRgb(0x2F, 0xC4, 0xD6));
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
        ApplyWindowStyles();
        if (SystemMonitor.Shared.Last is { } last)
        {
            Show(last);
        }
    }

    private void Row(string key, string label, Color color)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), MinWidth = 170 };
        var value = new TextBlock { Text = "…", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(14, 0, 0, 0) };
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        name.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        name.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD1, 0xDE)) });
        row.Children.Add(name);
        _values[key] = value;
        _rows.Children.Add(row);
    }

    private void Set(string key, string text)
    {
        if (_values.TryGetValue(key, out var block))
        {
            block.Text = text;
        }
    }

    private void Show(SystemSnapshot s)
    {
        Set("fps", s.Fps is { } fps ? MonitorFormat.Fps(fps) : "—");
        Set("cpu", MonitorFormat.Percent(s.CpuPercent));
        Set("gpu", MonitorFormat.Percent(s.GpuPercent));
        var temperatures = new List<string>();
        if (s.ThermalCelsius is { } cpu) temperatures.Add($"CPU {cpu:0}°");
        if (s.GpuCelsius is { } gpu) temperatures.Add($"GPU {gpu:0}°");
        Set("temp", temperatures.Count == 0 ? "—" : string.Join("  ", temperatures));
        Set("power", MonitorFormat.Watts(s.GpuWatts));
        Set("ram", $"{MonitorFormat.Percent(s.MemoryPercent)} · {StorageFormat.Bytes(s.MemoryUsed)}");
        Set("net", $"↓ {MonitorFormat.Rate(s.NetworkDownBytesPerSecond)}  ↑ {MonitorFormat.Rate(s.NetworkUpBytesPerSecond)}");
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
