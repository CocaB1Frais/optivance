using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Optivance.Desktop;

public sealed class TreemapView : FrameworkElement
{
    private const double HitCellSize = 96;
    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(16, 19, 26)));
    private static readonly Brush TileBorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(16, 19, 26)));
    private static readonly Pen TileBorderPen = Freeze(new Pen(TileBorderBrush, 1));
    private static readonly Pen SelectedPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(241, 247, 255))), 2.5));
    private static readonly Pen FocusPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(114, 224, 177))), 1));
    private static readonly IReadOnlyDictionary<string, Brush> CategoryBrushes = CreateCategoryBrushes();

    private IReadOnlyList<StorageRow> _items = [];
    private RenderSnapshot? _snapshot;
    private CancellationTokenSource? _layoutCancellation;
    private int _layoutGeneration;
    private int _selectedId = -1;
    private int _hoveredId = -1;

    public event Action<StorageRow>? TileClicked;
    public event Action<StorageRow>? DirectoryDoubleClicked;
    public event Action<double>? LayoutCompleted;
    public event Action<Exception>? LayoutFailed;

    public TreemapView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetBetweenShowDelay(this, 100);
        SizeChanged += (_, _) => ScheduleLayout();
        MouseLeave += (_, _) =>
        {
            _hoveredId = -1;
            ToolTip = null;
            Cursor = Cursors.Arrow;
        };
    }

    public void SetItems(IReadOnlyList<StorageRow> items)
    {
        _items = items;
        if (_selectedId >= 0 && !items.Any(item => item.Id == _selectedId))
        {
            _selectedId = -1;
        }
        ScheduleLayout();
    }

    public void SetSelectedId(int? id)
    {
        _selectedId = id ?? -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        var snapshot = _snapshot;
        if (snapshot is null)
        {
            if (IsKeyboardFocused)
            {
                drawingContext.DrawRectangle(null, FocusPen, new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
            }
            return;
        }

        drawingContext.DrawDrawing(snapshot.Drawing);
        foreach (var tile in snapshot.Tiles)
        {
            if (tile.Row.Id == _selectedId)
            {
                drawingContext.DrawRectangle(null, SelectedPen, tile.Bounds);
            }
        }

        var labelsDrawn = 0;
        foreach (var tile in snapshot.Tiles)
        {
            if (labelsDrawn >= 48)
            {
                break;
            }
            if (tile.Bounds.Width < 86 || tile.Bounds.Height < 28)
            {
                continue;
            }

            drawingContext.PushClip(new RectangleGeometry(tile.Bounds));
            var formatted = new FormattedText(
                tile.Row.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                10,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(24, tile.Bounds.Width - 12),
                MaxTextHeight = 14,
                Trimming = TextTrimming.CharacterEllipsis
            };
            drawingContext.DrawText(formatted, new Point(tile.Bounds.X + 6, tile.Bounds.Y + 5));
            drawingContext.Pop();
            labelsDrawn++;
        }

        if (IsKeyboardFocused)
        {
            drawingContext.DrawRectangle(null, FocusPen, new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = HitTest(e.GetPosition(this));
        if (tile is null)
        {
            Cursor = Cursors.Arrow;
            ToolTip = null;
            _hoveredId = -1;
            return;
        }

        Cursor = Cursors.Hand;
        if (_hoveredId != tile.Value.Row.Id)
        {
            _hoveredId = tile.Value.Row.Id;
            var row = tile.Value.Row;
            ToolTip = $"{row.Name}\n{row.Path}\n{row.LogicalText} · {row.Category} · {row.Kind}";
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var tile = HitTest(e.GetPosition(this));
        if (tile is null)
        {
            return;
        }

        Focus();
        _selectedId = tile.Value.Row.Id;
        InvalidateVisual();
        TileClicked?.Invoke(tile.Value.Row);
        if (e.ClickCount >= 2 && tile.Value.Row.IsDirectory)
        {
            DirectoryDoubleClicked?.Invoke(tile.Value.Row);
        }
        e.Handled = true;
    }

    private void ScheduleLayout()
    {
        var generation = Interlocked.Increment(ref _layoutGeneration);
        _layoutCancellation?.Cancel();
        _layoutCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _layoutCancellation = cancellation;
        var rows = _items;
        var width = ActualWidth;
        var height = ActualHeight;
        _ = RebuildAsync(generation, rows, width, height, cancellation.Token);
    }

    private async Task RebuildAsync(int generation, IReadOnlyList<StorageRow> rows, double width, double height, CancellationToken cancellationToken)
    {
        if (width < 2 || height < 2)
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(() => BuildSnapshot(rows, width, height, cancellationToken), cancellationToken);
            if (generation != Volatile.Read(ref _layoutGeneration) || cancellationToken.IsCancellationRequested)
            {
                return;
            }
            _snapshot = snapshot;
            InvalidateVisual();
            LayoutCompleted?.Invoke(snapshot.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // A newer directory or size replaced this layout request.
        }
        catch (Exception exception)
        {
            if (generation == Volatile.Read(ref _layoutGeneration))
            {
                LayoutFailed?.Invoke(exception);
            }
        }
    }

    private static RenderSnapshot BuildSnapshot(IReadOnlyList<StorageRow> rows, double width, double height, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var tiles = TreemapLayoutEngine.Calculate(rows, width, height, cancellationToken);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            for (var index = 0; index < tiles.Length; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var tile = tiles[index];
                if (tile.Bounds.Width <= 0 || tile.Bounds.Height <= 0)
                {
                    continue;
                }
                context.DrawRectangle(GetCategoryBrush(tile.Row.Category), TileBorderPen, tile.Bounds);
            }
        }
        drawing.Freeze();
        var cells = BuildHitGrid(tiles, width, height, cancellationToken, out var columns, out var rowCount);
        watch.Stop();
        return new RenderSnapshot(tiles, drawing, cells, columns, rowCount, watch.Elapsed.TotalMilliseconds);
    }

    private TreemapTile? HitTest(Point point)
    {
        var snapshot = _snapshot;
        if (snapshot is null || point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
        {
            return null;
        }

        var column = Math.Clamp((int)(point.X / HitCellSize), 0, snapshot.Columns - 1);
        var row = Math.Clamp((int)(point.Y / HitCellSize), 0, snapshot.Rows - 1);
        var candidates = snapshot.Cells[row * snapshot.Columns + column];
        foreach (var index in candidates)
        {
            if (snapshot.Tiles[index].Bounds.Contains(point))
            {
                return snapshot.Tiles[index];
            }
        }
        return null;
    }

    private static int[][] BuildHitGrid(TreemapTile[] tiles, double width, double height, CancellationToken cancellationToken, out int columns, out int rows)
    {
        columns = Math.Max(1, (int)Math.Ceiling(width / HitCellSize));
        rows = Math.Max(1, (int)Math.Ceiling(height / HitCellSize));
        var buckets = new List<int>?[columns * rows];
        for (var index = 0; index < tiles.Length; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var bounds = tiles[index].Bounds;
            var minColumn = Math.Clamp((int)(bounds.Left / HitCellSize), 0, columns - 1);
            var maxColumn = Math.Clamp((int)(Math.Max(bounds.Left, bounds.Right - 0.001) / HitCellSize), 0, columns - 1);
            var minRow = Math.Clamp((int)(bounds.Top / HitCellSize), 0, rows - 1);
            var maxRow = Math.Clamp((int)(Math.Max(bounds.Top, bounds.Bottom - 0.001) / HitCellSize), 0, rows - 1);
            for (var row = minRow; row <= maxRow; row++)
            {
                for (var column = minColumn; column <= maxColumn; column++)
                {
                    var cell = row * columns + column;
                    (buckets[cell] ??= []).Add(index);
                }
            }
        }

        return buckets.Select(bucket => bucket?.ToArray() ?? []).ToArray();
    }

    private static Brush GetCategoryBrush(string category) => CategoryBrushes.TryGetValue(category, out var brush) ? brush : CategoryBrushes["Autres"];

    private static IReadOnlyDictionary<string, Brush> CreateCategoryBrushes() => new Dictionary<string, Brush>(StringComparer.Ordinal)
    {
        ["Dossiers"] = Freeze(new SolidColorBrush(Color.FromRgb(75, 111, 156))),
        ["Images"] = Freeze(new SolidColorBrush(Color.FromRgb(66, 145, 192))),
        ["Vidéos"] = Freeze(new SolidColorBrush(Color.FromRgb(131, 103, 185))),
        ["Audio"] = Freeze(new SolidColorBrush(Color.FromRgb(191, 119, 70))),
        ["Documents"] = Freeze(new SolidColorBrush(Color.FromRgb(71, 149, 112))),
        ["Archives"] = Freeze(new SolidColorBrush(Color.FromRgb(173, 145, 63))),
        ["Applications"] = Freeze(new SolidColorBrush(Color.FromRgb(186, 82, 101))),
        ["Code"] = Freeze(new SolidColorBrush(Color.FromRgb(53, 145, 145))),
        ["Autres"] = Freeze(new SolidColorBrush(Color.FromRgb(81, 95, 115)))
    };

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze)
        {
            freezable.Freeze();
        }
        return freezable;
    }

    private sealed record RenderSnapshot(TreemapTile[] Tiles, DrawingGroup Drawing, int[][] Cells, int Columns, int Rows, double ElapsedMilliseconds);
}
