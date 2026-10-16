using System.Windows;
using System.Windows.Media;
using FSHWMonitor.Core;

namespace FSHWMonitor.Controls;

/// <summary>Auto-scaled area chart of a <see cref="HistoryBuffer"/>, newest sample at the right edge.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty BufferProperty = DependencyProperty.Register(
        nameof(Buffer), typeof(HistoryBuffer), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bump to redraw after the buffer changes (the buffer itself is mutable).</summary>
    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Colors.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Sparkline)d)._brushes = null));

    public static readonly DependencyProperty GridColorProperty = DependencyProperty.Register(
        nameof(GridColor), typeof(Color), typeof(Sparkline),
        new FrameworkPropertyMetadata(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Sparkline)d)._gridPen = null));

    /// <summary>Smallest vertical span, so a flat line doesn't turn sensor noise into mountains.</summary>
    public static readonly DependencyProperty MinSpanProperty = DependencyProperty.Register(
        nameof(MinSpan), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public HistoryBuffer? Buffer { get => (HistoryBuffer?)GetValue(BufferProperty); set => SetValue(BufferProperty, value); }
    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public double MinSpan { get => (double)GetValue(MinSpanProperty); set => SetValue(MinSpanProperty, value); }
    public Color GridColor { get => (Color)GetValue(GridColorProperty); set => SetValue(GridColorProperty, value); }

    Pen? _gridPen;
    (Brush Fill, Pen Line, Brush Dot, Brush Halo)? _brushes;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        _gridPen ??= CreateGridPen(GridColor);
        for (int i = 0; i <= 2; i++)
        {
            double y = Math.Round((h - 1) * i / 2) + 0.5;
            dc.DrawLine(_gridPen, new Point(0, y), new Point(w, y));
        }

        var buffer = Buffer;
        if (buffer is null || buffer.Count < 2) return;

        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < buffer.Count; i++)
        {
            min = Math.Min(min, buffer[i]);
            max = Math.Max(max, buffer[i]);
        }
        double span = Math.Max(max - min, MinSpan) * 1.2;
        double mid = (max + min) / 2;
        double low = mid - span / 2;

        double step = w / (buffer.Capacity - 1);
        double x0 = w - (buffer.Count - 1) * step;
        var points = new Point[buffer.Count];
        for (int i = 0; i < buffer.Count; i++)
            points[i] = new Point(x0 + i * step, h - (buffer[i] - low) / span * h);

        _brushes ??= CreateBrushes(LineColor);
        var (fill, linePen, dot, halo) = _brushes.Value;

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, h), true, true);
            g.PolyLineTo(points, false, true);
            g.LineTo(new Point(points[^1].X, h), false, false);
        }
        area.Freeze();
        dc.DrawGeometry(fill, null, area);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points[1..], true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, linePen, line);

        var last = points[^1];
        dc.DrawEllipse(halo, null, last, 7, 7);
        dc.DrawEllipse(dot, null, last, 3.5, 3.5);
    }

    static (Brush, Pen, Brush, Brush) CreateBrushes(Color c)
    {
        var fill = new LinearGradientBrush(Color.FromArgb(0x50, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
        fill.Freeze();
        var stroke = new SolidColorBrush(c);
        stroke.Freeze();
        var pen = new Pen(stroke, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var halo = new SolidColorBrush(Color.FromArgb(0x40, c.R, c.G, c.B));
        halo.Freeze();
        return (fill, pen, stroke, halo);
    }

    static Pen CreateGridPen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        pen.Freeze();
        return pen;
    }
}
