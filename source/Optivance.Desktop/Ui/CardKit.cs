using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Optivance.Core;

namespace Optivance.Desktop.Ui;

/// <summary>Briques visuelles des pages en cartes (Optimisation, Nettoyage).</summary>
internal static class CardKit
{
    public static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
    public static Brush Muted => Resource("MutedTextBrush");
    public static Brush Text => Resource("TextBrush");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly Brush CardBrush = Frozen(Color.FromRgb(0x1B, 0x20, 0x2A));
    public static readonly Brush CardHoverBrush = Frozen(Color.FromRgb(0x20, 0x26, 0x32));
    public static readonly Brush CardBorderBrush = Frozen(Color.FromRgb(0x2A, 0x33, 0x41));
    public static readonly Brush ChipBrush = Frozen(Color.FromRgb(0x24, 0x2B, 0x37));
    private static readonly Color SafeColor = Color.FromRgb(0x5C, 0xD6, 0x8A);
    private static readonly Color ModerateColor = Color.FromRgb(0xF2, 0xB8, 0x4B);
    private static readonly Color RiskyColor = Color.FromRgb(0xF2, 0x6D, 0x6D);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static TextBlock Icon(string glyph, double size = 12, Brush? brush = null) =>
        new() { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = brush ?? Muted, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Étiquette discrète : icône + texte (ex. « Votre compte », « Jeux »).</summary>
    public static Border Chip(string glyph, string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Icon(glyph, 11));
        line.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Foreground = Muted, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return new Border { Background = ChipBrush, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 8, 3), Margin = new Thickness(0, 0, 6, 0), Child = line };
    }

    /// <summary>Badge de niveau de risque, pleine couleur translucide.</summary>
    public static Border RiskBadge(TweakRisk risk)
    {
        var (text, glyph, color) = risk switch
        {
            TweakRisk.Safe => ("Sans risque", "", SafeColor),
            TweakRisk.Moderate => ("Modéré", "", ModerateColor),
            _ => ("Risqué", "", RiskyColor)
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Icon(glyph, 11, Frozen(color)));
        line.Children.Add(new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Frozen(color), Margin = new Thickness(5, 0, 0, 0) });
        return new Border
        {
            Background = Frozen(Color.FromArgb(0x26, color.R, color.G, color.B)),
            BorderBrush = Frozen(Color.FromArgb(0x55, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 2, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = line
        };
    }

    /// <summary>Carte : fond légèrement relevé, coins arrondis, survol.</summary>
    public static Border Card(UIElement content)
    {
        var card = new Border
        {
            Background = CardBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 16, 12),
            Margin = new Thickness(0, 0, 14, 14),
            Child = content
        };
        card.MouseEnter += (_, _) => card.Background = CardHoverBrush;
        card.MouseLeave += (_, _) => card.Background = CardBrush;
        return card;
    }

    public static CheckBox Switch(bool value, bool enabled = true)
    {
        var toggle = new CheckBox { IsChecked = value, IsEnabled = enabled, Style = (Style)Application.Current.FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
        // Indisponible (autorisation requise…) : nettement grisé.
        toggle.IsEnabledChanged += (_, _) => toggle.Opacity = toggle.IsEnabled ? 1 : 0.35;
        toggle.Opacity = enabled ? 1 : 0.35;
        return toggle;
    }

    /// <summary>Champ de recherche avec loupe et texte d'aide.</summary>
    public static Border SearchBox(string hint, Action<string> changed)
    {
        var box = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Text, CaretBrush = Resource("AccentBrush"), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        var placeholder = new TextBlock { Text = hint, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        box.TextChanged += (_, _) =>
        {
            placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            changed(box.Text);
        };
        var grid = new Grid();
        grid.Children.Add(placeholder);
        grid.Children.Add(box);
        var line = new DockPanel();
        var icon = Icon("", 12);
        icon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(grid);
        return new Border { Background = CardBrush, BorderBrush = CardBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 6, 10, 6), Width = 240, Child = line, Margin = new Thickness(0, 0, 10, 0) };
    }

    /// <summary>Bouton de filtre à bascule (ex. « Masquer les réglages appliqués »).</summary>
    public static ToggleButtonChip FilterToggle(string glyph, string text, Action<bool> changed) => new(glyph, text, changed);
}

/// <summary>Bouton-pastille qui reste enfoncé (filtre activé).</summary>
internal sealed class ToggleButtonChip : Border
{
    private bool _on;

    public ToggleButtonChip(string glyph, string text, Action<bool> changed)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(CardKit.Icon(glyph, 12));
        line.Children.Add(new TextBlock { Text = text, Foreground = CardKit.Text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Child = line;
        CornerRadius = new CornerRadius(7);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 6, 12, 6);
        Margin = new Thickness(0, 0, 10, 0);
        Cursor = Cursors.Hand;
        Refresh();
        MouseLeftButtonUp += (_, _) =>
        {
            _on = !_on;
            Refresh();
            changed(_on);
        };
    }

    private void Refresh()
    {
        Background = _on ? CardKit.Resource("SelectedSurfaceBrush") : CardKit.CardBrush;
        BorderBrush = _on ? CardKit.Resource("AccentStrongBrush") : CardKit.CardBorderBrush;
    }
}

/// <summary>Onglets texte soulignés par une barre de couleur (style des pages en cartes).</summary>
internal sealed class TabStrip : StackPanel
{
    private readonly List<(TextBlock Label, Border Bar)> _tabs = [];

    public event Action<int>? Selected;

    public int SelectedIndex { get; private set; }

    public TabStrip(IEnumerable<string> labels)
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 0, 0, 14);
        var index = 0;
        foreach (var text in labels)
        {
            var current = index++;
            var label = new TextBlock { Text = text, FontSize = 13.5, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
            var bar = new Border { Height = 3, Width = 22, CornerRadius = new CornerRadius(2), Background = CardKit.Resource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 24, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            stack.Children.Add(label);
            stack.Children.Add(bar);
            stack.MouseLeftButtonUp += (_, _) => Select(current);
            Children.Add(stack);
            _tabs.Add((label, bar));
        }
        Select(0, notify: false);
    }

    public void Select(int index, bool notify = true)
    {
        SelectedIndex = index;
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Label.Foreground = i == index ? CardKit.Text : CardKit.Muted;
            _tabs[i].Label.FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Normal;
            _tabs[i].Bar.Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
        }
        if (notify)
        {
            Selected?.Invoke(index);
        }
    }
}

/// <summary>Grille de cartes : colonnes de largeur égale selon la place (une carte ≥ <see cref="MinimumCardWidth"/>).</summary>
internal sealed class CardPanel : Panel
{
    public double MinimumCardWidth { get; set; } = 420;

    private int Columns(double width) => double.IsInfinity(width) ? 2 : Math.Max(1, (int)(width / MinimumCardWidth));

    protected override Size MeasureOverride(Size available)
    {
        var columns = Columns(available.Width);
        var cellWidth = double.IsInfinity(available.Width) ? MinimumCardWidth : available.Width / columns;
        double height = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            double row = 0;
            for (var i = start; i < Math.Min(start + columns, InternalChildren.Count); i++)
            {
                InternalChildren[i].Measure(new Size(cellWidth, double.PositiveInfinity));
                row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            }
            height += row;
        }
        return new Size(double.IsInfinity(available.Width) ? cellWidth * columns : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var columns = Columns(final.Width);
        var cellWidth = final.Width / columns;
        double y = 0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);
            double row = 0;
            for (var i = start; i < end; i++)
            {
                row = Math.Max(row, InternalChildren[i].DesiredSize.Height);
            }
            for (var i = start; i < end; i++)
            {
                InternalChildren[i].Arrange(new Rect((i - start) * cellWidth, y, cellWidth, row));
            }
            y += row;
        }
        return final;
    }
}
