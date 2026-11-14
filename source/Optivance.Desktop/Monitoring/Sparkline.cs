using System.Windows;
using System.Windows.Media;

namespace Optivance.Desktop.Monitoring;

/// <summary>Courbe compacte des dernières valeurs (historique en mémoire uniquement).</summary>
public sealed class Sparkline : FrameworkElement
{
    private readonly Queue<double> _values = new();
    private readonly Pen _pen;
    private readonly Brush _fill;

    public Sparkline(Color color, int capacity = 60)
    {
        Capacity = capacity;
        _pen = new Pen(new SolidColorBrush(color), 1.6);
        _pen.Freeze();
        _fill = new LinearGradientBrush(Color.FromArgb(90, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        _fill.Freeze();
        Height = 44;
        SnapsToDevicePixels = true;
    }

    public int Capacity { get; }
    /// <summary>Maximum fixe (ex. 100 %) ; null = adapté aux valeurs.</summary>
    public double? Maximum { get; init; }

    public void Push(double? value)
    {
        _values.Enqueue(value ?? 0);
        while (_values.Count > Capacity)
        {
            _values.Dequeue();
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (_values.Count < 2 || width < 4 || height < 4)
        {
            return;
        }
        var values = _values.ToArray();
        var maximum = Maximum ?? Math.Max(1e-9, values.Max() * 1.15);
        var step = width / (Capacity - 1);
        var offset = width - (values.Length - 1) * step;
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var areaContext = area.Open())
        {
            for (var index = 0; index < values.Length; index++)
            {
                var point = new Point(offset + index * step, height - Math.Clamp(values[index] / maximum, 0, 1) * (height - 2) - 1);
                if (index == 0)
                {
                    lineContext.BeginFigure(point, false, false);
                    areaContext.BeginFigure(new Point(point.X, height), true, true);
                    areaContext.LineTo(point, false, false);
                }
                else
                {
                    lineContext.LineTo(point, true, true);
                    areaContext.LineTo(point, false, false);
                }
            }
            areaContext.LineTo(new Point(offset + (values.Length - 1) * step, height), false, false);
        }
        line.Freeze();
        area.Freeze();
        context.DrawGeometry(_fill, null, area);
        context.DrawGeometry(null, _pen, line);
    }
}
