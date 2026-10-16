using System.Windows;
using System.Windows.Media;

namespace FSHWMonitor.Theme;

/// <summary>Cached, frozen brushes for colors the view models compute at runtime.</summary>
public static class Palette
{
    static readonly Dictionary<(Color, byte), SolidColorBrush> SolidCache = [];
    static readonly Dictionary<(Color, double, string), Brush> CardBorderCache = [];

    public static SolidColorBrush Brush(Color color, byte alpha = 255)
    {
        if (!SolidCache.TryGetValue((color, alpha), out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            SolidCache[(color, alpha)] = brush;
        }
        return brush;
    }

    /// <summary>
    /// Card outline that glows in the given color at the top and fades into the theme's frame color.
    /// <paramref name="strength"/> 0 gives a plain frame (flat themes at rest).
    /// </summary>
    public static Brush CardBorder(Color color, double strength = 1)
    {
        var theme = ThemeManager.Current;
        var key = (color, strength, theme.Name);
        if (!CardBorderCache.TryGetValue(key, out var brush))
        {
            var top = strength <= 0
                ? theme.CardEdge
                : Color.FromArgb((byte)(0x90 * Math.Min(1, strength)), color.R, color.G, color.B);
            brush = new LinearGradientBrush(
                [
                    new GradientStop(top, 0),
                    new GradientStop(theme.CardEdge, 0.38),
                    new GradientStop(theme.CardEdgeBottom, 1),
                ],
                new Point(0, 0), new Point(0, 1));
            brush.Freeze();
            CardBorderCache[key] = brush;
        }
        return brush;
    }
}
