using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FSHWMonitor.Controls;

/// <summary>
/// 270° segmented ring gauge. Each segment's color comes from its place on the scale: the accent
/// color, fading to amber at <see cref="Warn"/> and red at <see cref="Crit"/>. Lit segments follow
/// an eased value, and small markers show the session low and high. Set <see cref="GlowOnly"/>
/// on a blurred copy behind the gauge to get the neon glow.
/// </summary>
public sealed class ArcGauge : FrameworkElement
{
    const double StartAngle = 135;
    const double SweepAngle = 270;
    const double SegmentGapDegrees = 1.9;
    static readonly FontFamily LabelFamily = new("Bahnschrift");

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ArcGauge), new PropertyMetadata(double.NaN, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = Visual(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = Visual(nameof(Maximum), 100.0);
    public static readonly DependencyProperty WarnProperty = Visual(nameof(Warn), 80.0);
    public static readonly DependencyProperty CritProperty = Visual(nameof(Crit), 90.0);
    public static readonly DependencyProperty LowMarkerProperty = Visual(nameof(LowMarker), double.NaN);
    public static readonly DependencyProperty HighMarkerProperty = Visual(nameof(HighMarker), double.NaN);
    public static readonly DependencyProperty AccentColorProperty = Visual(nameof(AccentColor), Colors.DeepSkyBlue);
    public static readonly DependencyProperty WarnColorProperty = Visual(nameof(WarnColor), Color.FromRgb(0xFB, 0xBF, 0x24));
    public static readonly DependencyProperty HotColorProperty = Visual(nameof(HotColor), Color.FromRgb(0xF4, 0x3F, 0x5E));
    public static readonly DependencyProperty LowMarkerColorProperty = Visual(nameof(LowMarkerColor), Color.FromRgb(0x7D, 0xD3, 0xFC));
    public static readonly DependencyProperty HighMarkerColorProperty = Visual(nameof(HighMarkerColor), Color.FromRgb(0xFB, 0x71, 0x85));
    public static readonly DependencyProperty TickColorProperty = Visual(nameof(TickColor), Colors.White);
    public static readonly DependencyProperty LabelColorProperty = Visual(nameof(LabelColor), Color.FromRgb(0x5B, 0x66, 0x78));
    public static readonly DependencyProperty DimAlphaProperty = Visual(nameof(DimAlpha), 0.12);
    public static readonly DependencyProperty GlowOnlyProperty = Visual(nameof(GlowOnly), false);
    public static readonly DependencyProperty SegmentCountProperty = Visual(nameof(SegmentCount), 40);
    public static readonly DependencyProperty RingThicknessProperty = Visual(nameof(RingThickness), 18.0);

    static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Warn { get => (double)GetValue(WarnProperty); set => SetValue(WarnProperty, value); }
    public double Crit { get => (double)GetValue(CritProperty); set => SetValue(CritProperty, value); }
    public double LowMarker { get => (double)GetValue(LowMarkerProperty); set => SetValue(LowMarkerProperty, value); }
    public double HighMarker { get => (double)GetValue(HighMarkerProperty); set => SetValue(HighMarkerProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public Color WarnColor { get => (Color)GetValue(WarnColorProperty); set => SetValue(WarnColorProperty, value); }
    public Color HotColor { get => (Color)GetValue(HotColorProperty); set => SetValue(HotColorProperty, value); }
    public Color LowMarkerColor { get => (Color)GetValue(LowMarkerColorProperty); set => SetValue(LowMarkerColorProperty, value); }
    public Color HighMarkerColor { get => (Color)GetValue(HighMarkerColorProperty); set => SetValue(HighMarkerColorProperty, value); }
    public Color TickColor { get => (Color)GetValue(TickColorProperty); set => SetValue(TickColorProperty, value); }
    public Color LabelColor { get => (Color)GetValue(LabelColorProperty); set => SetValue(LabelColorProperty, value); }
    public double DimAlpha { get => (double)GetValue(DimAlphaProperty); set => SetValue(DimAlphaProperty, value); }
    public bool GlowOnly { get => (bool)GetValue(GlowOnlyProperty); set => SetValue(GlowOnlyProperty, value); }
    public int SegmentCount { get => (int)GetValue(SegmentCountProperty); set => SetValue(SegmentCountProperty, value); }
    public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }

    // Geometry and pens are rebuilt only when size or appearance changes, not on every animation frame.
    bool _cacheValid;
    Point _center;
    double _outerRadius;
    double _segmentOuterRadius;
    Segment[] _segments = [];
    Geometry? _minorTicks, _majorTicks, _innerRing;
    (Geometry Geometry, Pen Pen)[] _zones = [];
    FormattedText? _minLabel, _maxLabel;
    Point _minLabelAt, _maxLabelAt;
    Pen? _minorTickPen, _majorTickPen, _innerRingPen;

    sealed record Segment(Geometry Geometry, Color Color, Pen LitPen, Pen DimPen);

    static DependencyProperty Visual(string name, object defaultValue) => DependencyProperty.Register(
        name, defaultValue.GetType(), typeof(ArcGauge),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((ArcGauge)d)._cacheValid = false));

    static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (ArcGauge)d;
        double target = (double)e.NewValue;

        if (double.IsNaN(target))
        {
            gauge.BeginAnimation(DisplayValueProperty, null);
            gauge.SetValue(DisplayValueProperty, double.NaN);
            return;
        }

        double from = (double)gauge.GetValue(DisplayValueProperty);
        if (double.IsNaN(from))
            from = gauge.Minimum;

        var animation = new DoubleAnimation(from, target, new Duration(TimeSpan.FromMilliseconds(650)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        // 30 fps is plenty for a gauge sweep and halves the render work of an always-on app.
        Timeline.SetDesiredFrameRate(animation, 30);
        animation.Freeze();
        gauge.BeginAnimation(DisplayValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _cacheValid = false;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // The scale labels use the inherited (theme) font.
        if (e.Property == TextElement.FontFamilyProperty)
        {
            _cacheValid = false;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 40) return;
        if (!_cacheValid) BuildCache(size);

        double min = Minimum, range = Math.Max(1e-6, Maximum - Minimum);
        double shown = (double)GetValue(DisplayValueProperty);
        double lit = double.IsNaN(shown) ? 0 : Math.Clamp((shown - min) / range, 0, 1) * _segments.Length;

        if (!GlowOnly)
        {
            dc.DrawGeometry(null, _minorTickPen, _minorTicks);
            dc.DrawGeometry(null, _majorTickPen, _majorTicks);
            foreach (var (geometry, pen) in _zones)
                dc.DrawGeometry(null, pen, geometry);
            dc.DrawGeometry(null, _innerRingPen, _innerRing);
            if (_minLabel != null) dc.DrawText(_minLabel, _minLabelAt);
            if (_maxLabel != null) dc.DrawText(_maxLabel, _maxLabelAt);
        }

        for (int i = 0; i < _segments.Length; i++)
        {
            var segment = _segments[i];
            double fill = Math.Clamp(lit - i, 0, 1);

            if (!GlowOnly)
                dc.DrawGeometry(null, segment.DimPen, segment.Geometry);
            if (fill <= 0.01)
                continue;

            var pen = fill >= 0.99 ? segment.LitPen : FrozenPen(WithAlpha(segment.Color, fill), RingThickness);
            dc.DrawGeometry(null, pen, segment.Geometry);
        }

        if (!GlowOnly)
        {
            DrawMarker(dc, LowMarker, LowMarkerColor);
            DrawMarker(dc, HighMarker, HighMarkerColor);
        }
    }

    void DrawMarker(DrawingContext dc, double value, Color color)
    {
        if (double.IsNaN(value)) return;
        double range = Math.Max(1e-6, Maximum - Minimum);
        double angle = StartAngle + Math.Clamp((value - Minimum) / range, 0, 1) * SweepAngle;

        double tip = _segmentOuterRadius + 2;
        double back = tip + 8;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointAt(tip, angle), true, true);
            g.LineTo(PointAt(back, angle - 2.8), true, true);
            g.LineTo(PointAt(back, angle + 2.8), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(FrozenBrush(color), null, geometry);
    }

    void BuildCache(double size)
    {
        _center = new Point(ActualWidth / 2, ActualHeight / 2);
        _outerRadius = size / 2 - 2;

        double thickness = RingThickness;
        _segmentOuterRadius = _outerRadius - 18;
        double segmentRadius = _segmentOuterRadius - thickness / 2;

        // Tick ring: 50 minor steps, a major tick every 10% of the scale.
        var minor = new StreamGeometry();
        var major = new StreamGeometry();
        using (var gMinor = minor.Open())
        using (var gMajor = major.Open())
        {
            for (int i = 0; i <= 50; i++)
            {
                double angle = StartAngle + SweepAngle * i / 50;
                bool isMajor = i % 5 == 0;
                var g = isMajor ? gMajor : gMinor;
                g.BeginFigure(PointAt(_outerRadius, angle), false, false);
                g.LineTo(PointAt(_outerRadius - (isMajor ? 8 : 4), angle), true, false);
            }
        }
        minor.Freeze();
        major.Freeze();
        _minorTicks = minor;
        _majorTicks = major;
        _minorTickPen = FrozenPen(WithAlpha(TickColor, 0.15), 1);
        _majorTickPen = FrozenPen(WithAlpha(TickColor, 0.33), 1.6);
        _innerRingPen = FrozenPen(WithAlpha(TickColor, 0.08), 1);

        // Warning and critical zones as a thin arc just inside the ticks.
        double range = Math.Max(1e-6, Maximum - Minimum);
        double AngleOf(double v) => StartAngle + Math.Clamp((v - Minimum) / range, 0, 1) * SweepAngle;
        double zoneRadius = _outerRadius - 12;
        var zones = new List<(Geometry, Pen)>();
        if (Crit > Warn)
            zones.Add((Arc(zoneRadius, AngleOf(Warn), AngleOf(Crit)), FrozenPen(WithAlpha(WarnColor, 0.7), 2.5)));
        if (Maximum > Crit)
            zones.Add((Arc(zoneRadius, AngleOf(Crit), AngleOf(Maximum)), FrozenPen(WithAlpha(HotColor, 0.8), 2.5)));
        _zones = zones.ToArray();

        var inner = new EllipseGeometry(_center, segmentRadius - thickness / 2 - 7, segmentRadius - thickness / 2 - 7);
        inner.Freeze();
        _innerRing = inner;

        // Segments.
        int count = Math.Max(8, SegmentCount);
        double step = SweepAngle / count;
        _segments = new Segment[count];
        for (int i = 0; i < count; i++)
        {
            double a1 = StartAngle + i * step + SegmentGapDegrees / 2;
            double a2 = StartAngle + (i + 1) * step - SegmentGapDegrees / 2;
            var color = ColorAt(Minimum + (i + 0.5) / count * range);
            _segments[i] = new Segment(
                Arc(segmentRadius, a1, a2),
                color,
                FrozenPen(color, thickness),
                FrozenPen(WithAlpha(color, DimAlpha), thickness));
        }

        // Scale end labels sit in the open bottom of the ring.
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        _minLabel = Label(Minimum, dpi);
        _maxLabel = Label(Maximum, dpi);
        var start = PointAt(segmentRadius, StartAngle);
        var end = PointAt(segmentRadius, StartAngle + SweepAngle);
        _minLabelAt = new Point(start.X - _minLabel.Width / 2, start.Y + thickness / 2 + 2);
        _maxLabelAt = new Point(end.X - _maxLabel.Width / 2, end.Y + thickness / 2 + 2);

        _cacheValid = true;
    }

    Color ColorAt(double value)
    {
        double warn = Warn, crit = Math.Max(Crit, Warn + 0.1);
        double fade = Math.Max(4, (crit - warn) * 0.8);
        if (value <= warn - fade) return AccentColor;
        if (value <= warn) return Lerp(AccentColor, WarnColor, (value - (warn - fade)) / fade);
        if (value <= crit) return Lerp(WarnColor, HotColor, (value - warn) / (crit - warn));
        return HotColor;
    }

    FormattedText Label(double value, double pixelsPerDip) => new(
        value.ToString("0", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this) ?? LabelFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        12, FrozenBrush(LabelColor), pixelsPerDip);

    Point PointAt(double radius, double angleDegrees)
    {
        double rad = angleDegrees * Math.PI / 180;
        return new Point(_center.X + radius * Math.Cos(rad), _center.Y + radius * Math.Sin(rad));
    }

    Geometry Arc(double radius, double fromAngle, double toAngle)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(PointAt(radius, fromAngle), false, false);
            g.ArcTo(PointAt(radius, toAngle), new Size(radius, radius), 0, toAngle - fromAngle > 180,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);

    static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        pen.Freeze();
        return pen;
    }

    static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
