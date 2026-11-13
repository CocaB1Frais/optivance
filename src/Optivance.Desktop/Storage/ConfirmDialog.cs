using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Optivance.Desktop.Storage;

/// <summary>Boîte de dialogue sombre (confirmation de suppression, information).</summary>
internal static class ConfirmDialog
{
    public static bool Ask(Window? owner, string title, string message, IReadOnlyList<(string Label, string Value)> details, string confirmText, bool danger)
    {
        var confirmed = false;
        var window = Create(owner, title, message, details, out var buttons);
        var cancel = MakeButton("Annuler", primary: false, danger: false);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => window.Close();
        var confirm = MakeButton(confirmText, primary: true, danger);
        confirm.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        window.Loaded += (_, _) => cancel.Focus();
        window.ShowDialog();
        return confirmed;
    }

    public static void Inform(Window? owner, string title, string message)
    {
        var window = Create(owner, title, message, [], out var buttons);
        var ok = MakeButton("Fermer", primary: true, danger: false);
        ok.IsDefault = true;
        ok.IsCancel = true;
        ok.Click += (_, _) => window.Close();
        buttons.Children.Add(ok);
        window.ShowDialog();
    }

    private static Window Create(Window? owner, string title, string message, IReadOnlyList<(string Label, string Value)> details, out StackPanel buttons)
    {
        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brush(0xEDF2F8) });
        stack.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Brush(0xB8C2D0), FontSize = 12.5 });
        if (details.Count > 0)
        {
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < details.Count; index++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = details[index].Label, Foreground = Brush(0x8794A8), Margin = new Thickness(0, 3, 16, 3), FontSize = 12 };
                var value = new TextBlock { Text = details[index].Value, Foreground = Brush(0xEDF2F8), Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                Grid.SetRow(label, index);
                Grid.SetRow(value, index);
                Grid.SetColumn(value, 1);
                grid.Children.Add(label);
                grid.Children.Add(value);
            }
            stack.Children.Add(new Border { Child = grid, Background = Brush(0x141922), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 12, 0, 0) });
        }
        buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        stack.Children.Add(buttons);

        var window = new Window
        {
            Owner = owner,
            Title = title,
            Content = new Border { Child = stack, Background = Brush(0x1A1F2A), BorderBrush = Brush(0x34404F), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) },
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 520,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                window.DragMove();
            }
        };
        return window;
    }

    private static Button MakeButton(string text, bool primary, bool danger)
    {
        var background = danger ? 0xC8443C : primary ? 0x2E765C : 0x262E3B;
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            Height = 34,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(16, 0, 16, 0),
            Foreground = Brush(0xF4F7FB),
            Background = Brush(background),
            BorderBrush = Brush(danger ? 0xE0675F : primary ? 0x72E0B1 : 0x3A4658),
            Cursor = Cursors.Hand
        };
        return button;
    }

    private static SolidColorBrush Brush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
