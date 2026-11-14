using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Optivance.Core;
using Optivance.Core.Storage;
using Optivance.Infrastructure.Monitoring;

namespace Optivance.Desktop.Monitoring;

/// <summary>
/// Monitoring : processeur, carte graphique, mémoire, disques, réseau, système. Les mesures ne
/// sont relevées que lorsque la page est visible ; les courbes gardent la dernière minute en mémoire.
/// </summary>
public sealed class MonitoringPage : UserControl
{
    private readonly SystemMonitor _monitor = SystemMonitor.Shared;
    private readonly Action _toggleWidget;
    private IDisposable? _subscription;
    private readonly Dictionary<string, TextBlock> _values = [];
    private readonly Sparkline _cpuLine = new(Color.FromRgb(0x4F, 0x8D, 0xF5)) { Maximum = 100 };
    private readonly Sparkline _gpuLine = new(Color.FromRgb(0x3C, 0xC4, 0x8A)) { Maximum = 100 };
    private readonly Sparkline _memoryLine = new(Color.FromRgb(0x9B, 0x6C, 0xF0)) { Maximum = 100 };
    private readonly Sparkline _diskLine = new(Color.FromRgb(0xF2, 0xB3, 0x3D)) { Maximum = 100 };
    private readonly Sparkline _networkLine = new(Color.FromRgb(0x2F, 0xC4, 0xD6));
    private readonly StackPanel _volumes = new();
    private readonly Button _widgetButton = new() { Padding = new Thickness(14, 8, 14, 8) };

    public MonitoringPage(Action toggleWidget)
    {
        _toggleWidget = toggleWidget;
        Background = (Brush)Application.Current.FindResource("WindowBrush");
        var muted = (Brush)Application.Current.FindResource("MutedTextBrush");

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Monitoring", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "État du PC en direct, sans pilote ni service ; rien n’est enregistré sur le disque.", Foreground = muted, Margin = new Thickness(1, 3, 0, 0) });
        _widgetButton.Click += (_, _) =>
        {
            _toggleWidget();
            RefreshWidgetButton();
        };
        DockPanel.SetDock(_widgetButton, Dock.Right);
        header.Children.Add(_widgetButton);
        header.Children.Add(titles);

        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(Card("Processeur", _monitor.CpuName, _cpuLine,
            ("cpu", "Utilisation"), ("cpuFreq", "Fréquence"), ("cpuTemp", "Température")));
        grid.Children.Add(Card("Carte graphique", "—", _gpuLine,
            ("gpu", "Utilisation"), ("gpuTemp", "Température"), ("gpuPower", "Consommation"), ("gpuClock", "Fréquence"), ("gpuMemory", "Mémoire vidéo")));
        grid.Children.Add(Card("Mémoire", "Mémoire vive", _memoryLine, ("memory", "Utilisée"), ("memoryPercent", "Occupation")));
        grid.Children.Add(Card("Disques", "Tous les disques", _diskLine, ("diskActive", "Activité"), ("diskRead", "Lecture"), ("diskWrite", "Écriture")));
        grid.Children.Add(Card("Réseau", "Toutes les interfaces", _networkLine, ("netDown", "Réception"), ("netUp", "Envoi")));
        grid.Children.Add(Card("Système", Environment.OSVersion.VersionString, null, ("uptime", "Allumé depuis"), ("processes", "Processus"), ("fps", "FPS (jeu au premier plan)")));

        var volumesCard = new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 10) };
        var volumesPanel = new StackPanel();
        volumesPanel.Children.Add(new TextBlock { Text = "ESPACE DES VOLUMES", Style = (Style)Application.Current.FindResource("Caption"), Margin = new Thickness(0, 0, 0, 8) });
        volumesPanel.Children.Add(_volumes);
        volumesCard.Child = volumesPanel;

        var content = new StackPanel { Margin = new Thickness(28, 22, 18, 20) };
        content.Children.Add(header);
        content.Children.Add(grid);
        content.Children.Add(volumesCard);
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _monitor.IntervalMilliseconds = App.Settings.MonitoringRefreshMilliseconds;
                _subscription ??= _monitor.Subscribe(snapshot => Dispatcher.BeginInvoke(() => Show(snapshot)));
                RefreshVolumes();
                RefreshWidgetButton();
            }
            else
            {
                _subscription?.Dispose();
                _subscription = null;
            }
        };
    }

    public void RefreshWidgetButton() =>
        _widgetButton.Content = App.Settings.WidgetVisible ? "Masquer le widget" : "Afficher le widget";

    private Border Card(string title, string subtitle, Sparkline? line, params (string Key, string Label)[] rows)
    {
        var muted = (Brush)Application.Current.FindResource("MutedTextBrush");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Style = (Style)Application.Current.FindResource("Caption") });
        var subtitleText = new TextBlock { Text = subtitle, FontSize = 12, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 8) };
        _values[title + ":subtitle"] = subtitleText;
        panel.Children.Add(subtitleText);
        var first = true;
        foreach (var (key, label) in rows)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var value = new TextBlock { FontSize = first ? 24 : 13, FontWeight = first ? FontWeights.SemiBold : FontWeights.Normal, Text = "…" };
            _values[key] = value;
            DockPanel.SetDock(value, Dock.Right);
            row.Children.Add(value);
            row.Children.Add(new TextBlock { Text = label, Foreground = muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
            panel.Children.Add(row);
            first = false;
        }
        if (line is not null)
        {
            line.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(line);
        }
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 10, 10), Child = panel };
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
        Set("cpu", MonitorFormat.Percent(s.CpuPercent));
        Set("cpuFreq", MonitorFormat.Ghz(s.CpuFrequencyMhz));
        Set("cpuTemp", MonitorFormat.Celsius(s.ThermalCelsius));
        if (_values.TryGetValue("cpuTemp", out var cpuTemp) && s.ThermalCelsius is null)
        {
            cpuTemp.ToolTip = "Windows ne fournit pas la température du processeur sur ce PC sans pilote (OPTIVANCE n’installe aucun pilote).";
        }
        Set("Carte graphique:subtitle", s.GpuName ?? "Carte graphique (compteurs Windows)");
        Set("gpu", MonitorFormat.Percent(s.GpuPercent));
        Set("gpuTemp", MonitorFormat.Celsius(s.GpuCelsius));
        Set("gpuPower", MonitorFormat.Watts(s.GpuWatts));
        Set("gpuClock", MonitorFormat.Mhz(s.GpuClockMhz));
        Set("gpuMemory", MonitorFormat.Memory(s.GpuMemoryUsed, s.GpuMemoryTotal));
        Set("memory", StorageFormat.Bytes(s.MemoryUsed));
        Set("memoryPercent", $"{MonitorFormat.Percent(s.MemoryPercent)} de {StorageFormat.Bytes(s.MemoryTotal)}");
        Set("diskActive", MonitorFormat.Percent(s.DiskActivePercent));
        Set("diskRead", MonitorFormat.Rate(s.DiskReadBytesPerSecond));
        Set("diskWrite", MonitorFormat.Rate(s.DiskWriteBytesPerSecond));
        Set("netDown", MonitorFormat.Rate(s.NetworkDownBytesPerSecond));
        Set("netUp", MonitorFormat.Rate(s.NetworkUpBytesPerSecond));
        Set("uptime", MonitorFormat.Uptime(s.Uptime));
        Set("processes", s.Processes?.ToString() ?? MonitorFormat.Unavailable);
        Set("fps", s.Fps is { } fps ? $"{MonitorFormat.Fps(fps)} ({s.FpsProcess})" : "aucun jeu détecté");
        _cpuLine.Push(s.CpuPercent);
        _gpuLine.Push(s.GpuPercent);
        _memoryLine.Push(s.MemoryPercent);
        _diskLine.Push(s.DiskActivePercent);
        _networkLine.Push((s.NetworkDownBytesPerSecond ?? 0) + (s.NetworkUpBytesPerSecond ?? 0));
    }

    private void RefreshVolumes()
    {
        _volumes.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }
                var used = (ulong)(drive.TotalSize - drive.TotalFreeSpace);
                var fraction = drive.TotalSize == 0 ? 0 : (double)used / drive.TotalSize;
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Disque local" : drive.VolumeLabel;
                row.Children.Add(new TextBlock { Text = $"{label} ({drive.Name.TrimEnd('\\')})", VerticalAlignment = VerticalAlignment.Center });
                var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x40)), VerticalAlignment = VerticalAlignment.Center };
                var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Background = fraction > 0.9 ? new SolidColorBrush(Color.FromRgb(0xE8, 0x61, 0x5A)) : (Brush)Application.Current.FindResource("AccentBrush") };
                bar.Child = fill;
                bar.SizeChanged += (_, args) => fill.Width = args.NewSize.Width * fraction;
                Grid.SetColumn(bar, 1);
                row.Children.Add(bar);
                var detail = new TextBlock { Text = $"{StorageFormat.Bytes((ulong)drive.TotalFreeSpace)} libres sur {StorageFormat.Bytes((ulong)drive.TotalSize)}", Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(detail, 2);
                row.Children.Add(detail);
                _volumes.Children.Add(row);
            }
            catch (IOException)
            {
                // Lecteur retiré.
            }
        }
    }
}
