using System.Windows;
using FSHWMonitor.Core;

namespace FSHWMonitor.Platform;

/// <summary>How each card is laid out internally.</summary>
public enum CardStyle
{
    /// <summary>Tall card: gauge on top, stats, details and history below (landscape).</summary>
    Column,

    /// <summary>Wide card: gauge, then stats and history, then details (portrait).</summary>
    Row,

    /// <summary>Compact card: gauge beside everything else (strip and square displays).</summary>
    Compact,
}

/// <summary>
/// The dashboard's design canvas and card arrangement for one display. The canvas takes the display's
/// shape (within limits), so 1024×600 or 1920×480 panels fill edge to edge instead of letterboxing.
/// </summary>
public sealed record LayoutPlan(
    LayoutMode Mode,
    Size Canvas,
    CardStyle CardStyle,
    int Rows,
    int Columns,
    Thickness CardMargin,
    Thickness HostMargin)
{
    /// <summary>Narrow canvases put the footer banner on its own line.</summary>
    public bool Compact => Canvas.Width < 1000;

    /// <summary>Very narrow canvases (tall strips) also stack the footer.</summary>
    public bool Narrow => Canvas.Width < 700;
}

public static class LayoutPlanner
{
    /// <summary>The layout "Auto" picks for a display of the given width/height ratio.</summary>
    public static LayoutMode FromAspect(double? aspect) => aspect switch
    {
        null => LayoutMode.Landscape,
        >= 2.5 => LayoutMode.Bar,       // 1920×480, 2560×720 strips
        >= 1.2 => LayoutMode.Landscape, // 1280×800, 1024×600, 16:9
        > 0.85 => LayoutMode.Grid,      // square-ish
        > 0.4 => LayoutMode.Portrait,   // 800×1280
        _ => LayoutMode.TallBar,        // 480×1920
    };

    public static LayoutPlan Plan(LayoutMode requested, double? aspect, int cardCount)
    {
        var mode = requested == LayoutMode.Auto ? FromAspect(aspect) : requested;
        int n = Math.Max(1, cardCount);

        switch (mode)
        {
            case LayoutMode.Portrait:
            {
                double height = Fit(800 / (aspect ?? 0.625), 230 * n + 150, 2000);
                return new(mode, new Size(800, height), CardStyle.Row, n, 1, new Thickness(0, 6, 0, 6), new Thickness(20, 0, 20, 0));
            }
            case LayoutMode.Bar:
            {
                double width = Fit(480 * (aspect ?? 4.0), 390 * n + 40, 3000);
                return new(mode, new Size(width, 480), CardStyle.Compact, 1, n, new Thickness(7, 0, 7, 0), new Thickness(19, 0, 19, 0));
            }
            case LayoutMode.TallBar:
            {
                double height = Fit(600 / (aspect ?? 0.25), 420 * n + 160, 3000);
                return new(mode, new Size(600, height), CardStyle.Compact, n, 1, new Thickness(0, 6, 0, 6), new Thickness(20, 0, 20, 0));
            }
            case LayoutMode.Grid:
            {
                int rows = (n + 1) / 2;
                double height = Math.Max(1000, 420 * rows + 140);
                double width = Fit(height * (aspect ?? 1.0), 900, 1400);
                return new(mode, new Size(width, height), CardStyle.Compact, rows, n == 1 ? 1 : 2, new Thickness(7, 6, 7, 6), new Thickness(19, 0, 19, 0));
            }
            default:
            {
                double width = Fit(800 * (aspect ?? 1.6), 245 * n + 40, 2000);
                return new(LayoutMode.Landscape, new Size(width, 800), CardStyle.Column, 1, n, new Thickness(7, 0, 7, 0), new Thickness(19, 0, 19, 0));
            }
        }
    }

    /// <summary>Uses the display's own proportion when it's in range, else the nearest limit (and the view letterboxes).</summary>
    static double Fit(double ideal, double min, double max) => Math.Round(Math.Clamp(ideal, Math.Min(min, max), max));
}
