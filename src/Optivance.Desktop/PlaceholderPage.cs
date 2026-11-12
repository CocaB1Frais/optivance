using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Optivance.Desktop;

public static class PlaceholderPage
{
    public static UIElement Create(string title, string headline, string description)
    {
        var stack = new StackPanel { Margin = new Thickness(42, 38, 42, 36), VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(114, 224, 177)), FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = headline, FontSize = 31, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 4) });
        stack.Children.Add(new Border
        {
            Margin = new Thickness(0, 28, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(23, 27, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 59, 75)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "En développement", FontSize = 16, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(156, 168, 186)), Margin = new Thickness(0, 10, 0, 0) }
                }
            }
        });
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
