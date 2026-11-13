using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Optivance.Core.Storage;

namespace Optivance.Desktop.Storage;

/// <summary>
/// Treemap à coussins de l'arbre (ou d'un dossier zoomé). Le calcul des pixels est fait hors
/// du thread d'interface ; le survol, le clic et le menu contextuel utilisent la carte des
/// propriétaires de pixels (test de position immédiat).
/// </summary>
internal sealed class CushionTreemapView : FrameworkElement
{
    private static readonly Pen SelectionOuter = Freeze(new Pen(Brushes.White, 2));
    private static readonly Pen SelectionInner = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(16, 19, 26)), 1));
    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(20, 23, 30)));

    private StorageTree? _tree;
    private int _rootId;
    private int? _selectedId;
    private Func<int, int> _colorOf = _ => 0x808080;
    private TreemapFrame? _frame;
    private WriteableBitmap? _bitmap;
    private double _scaleX = 1, _scaleY = 1;
    private CancellationTokenSource? _render;
    private int _generation;
    private int _hovered = -1;

    public event Action<int>? ItemClicked;
    public event Action<int>? ItemDoubleClicked;
    public event Action<int>? ItemRightClicked;
    public event Action<int>? HoverChanged;
    public event Action<double>? Rendered;

    public CushionTreemapView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) => ScheduleRender(TimeSpan.FromMilliseconds(120));
    }

    public int RootId => _rootId;

    public void SetData(StorageTree? tree, int rootId, Func<int, int> colorOf)
    {
        _tree = tree;
        _rootId = rootId;
        _colorOf = colorOf;
        ScheduleRender(TimeSpan.Zero);
    }

    public void Refresh(Func<int, int>? colorOf = null)
    {
        if (colorOf is not null)
        {
            _colorOf = colorOf;
        }
        ScheduleRender(TimeSpan.Zero);
    }

    public void SetSelected(int? id)
    {
        _selectedId = id;
        InvalidateVisual();
    }

    private async void ScheduleRender(TimeSpan delay)
    {
        _render?.Cancel();
        var cancellation = new CancellationTokenSource();
        _render = cancellation;
        var generation = ++_generation;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellation.Token);
            }
            var tree = _tree;
            if (tree is null || ActualWidth < 4 || ActualHeight < 4)
            {
                _frame = null;
                _bitmap = null;
                InvalidateVisual();
                return;
            }
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
            var height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
            var root = _rootId;
            var colorOf = _colorOf;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var frame = await Task.Run(() => CushionTreemap.Render(tree, root, width, height, colorOf, cancellation.Token), cancellation.Token);
            if (generation != _generation)
            {
                return;
            }
            var bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), frame.Pixels, width * 4, 0);
            bitmap.Freeze();
            _frame = frame;
            _bitmap = bitmap;
            _scaleX = dpi.DpiScaleX;
            _scaleY = dpi.DpiScaleY;
            InvalidateVisual();
            Rendered?.Invoke(started.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Rendu remplacé par un plus récent.
        }
    }

    protected override void OnRender(DrawingContext context)
    {
        context.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_bitmap is null || _frame is null)
        {
            return;
        }
        context.DrawImage(_bitmap, new Rect(0, 0, _frame.Width / _scaleX, _frame.Height / _scaleY));
        if (_selectedId is { } selected && _tree is not null)
        {
            // Élément sélectionné, ou son ancêtre visible le plus proche.
            for (var current = selected; current >= 0; current = _tree.Parent(current))
            {
                if (_frame.Bounds.TryGetValue(current, out var bounds))
                {
                    var rect = new Rect(bounds.X / _scaleX, bounds.Y / _scaleY, Math.Max(2, bounds.Width / _scaleX), Math.Max(2, bounds.Height / _scaleY));
                    context.DrawRectangle(null, SelectionOuter, rect);
                    rect.Inflate(-2, -2);
                    if (rect.Width > 2 && rect.Height > 2)
                    {
                        context.DrawRectangle(null, SelectionInner, rect);
                    }
                    break;
                }
            }
        }
    }

    private int Hit(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        return _frame?.HitTest((int)(point.X * _scaleX), (int)(point.Y * _scaleY)) ?? -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = Hit(e);
        if (hit != _hovered)
        {
            _hovered = hit;
            HoverChanged?.Invoke(hit);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        HoverChanged?.Invoke(-1);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var hit = Hit(e);
        if (hit < 0)
        {
            return;
        }
        if (e.ClickCount >= 2)
        {
            ItemDoubleClicked?.Invoke(hit);
        }
        else
        {
            ItemClicked?.Invoke(hit);
        }
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var hit = Hit(e);
        if (hit >= 0)
        {
            ItemRightClicked?.Invoke(hit);
            e.Handled = true;
        }
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
