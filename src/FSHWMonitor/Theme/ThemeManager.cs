using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FSHWMonitor.Theme;

/// <summary>
/// Publishes the current theme as application resources (the XAML uses DynamicResource, so a
/// switch repaints instantly) and tells view models to refresh the colors they compute.
/// </summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = Themes.Default;

    public static event Action? Changed;

    /// <summary>Brand fonts for the menu and dialogs, which keep the Full Send look whatever the dashboard theme.</summary>
    public static void InstallBrandFonts()
    {
        var r = Application.Current.Resources;
        r["BrandTitleFont"] = Font("embedded:Poppins");
        r["BrandBodyFont"] = Font("embedded:PT Sans");
    }

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var r = Application.Current.Resources;

        r["WindowBackgroundBrush"] = Solid(theme.BackdropBottom);
        r["BackdropBrush"] = Vertical(theme.BackdropTop, theme.BackdropBottom);
        r["BackdropGlowABrush"] = Glow(theme.GlowA, new Point(0.08, 0), 0.55, 0.8);
        r["BackdropGlowBBrush"] = Glow(theme.GlowB, new Point(0.95, 1), 0.55, 0.75);

        r["CardBackgroundBrush"] = Vertical(theme.CardTop, theme.CardBottom);
        r["InsetBrush"] = Solid(theme.Inset);
        r["InsetBorderBrush"] = Solid(theme.InsetBorder);
        r["DividerBrush"] = Solid(theme.Divider);
        r["HairlineBrush"] = Solid(theme.Hairline);
        r["ChartBrush"] = Solid(theme.Chart);
        r["ChartBorderBrush"] = Solid(theme.ChartBorder);
        r["PillBrush"] = Solid(theme.Pill);
        r["PillBorderBrush"] = Solid(theme.PillBorder);

        r["TextPrimaryBrush"] = Solid(theme.TextPrimary);
        r["TextSecondaryBrush"] = Solid(theme.TextSecondary);
        r["MutedBrush"] = Solid(theme.Muted);
        r["WarmBrush"] = Solid(theme.Warm);
        r["HighStatBrush"] = Solid(theme.HighStat);
        r["LowStatBrush"] = Solid(theme.LowStat);
        r["SpectrumBrush"] = Spectrum(theme);

        r["MutedColor"] = theme.Muted;
        r["WarmColor"] = theme.Warm;
        r["HotColor"] = theme.Hot;
        r["HighStatColor"] = theme.HighStat;
        r["LowStatColor"] = theme.LowStat;
        r["TickColor"] = theme.Tick;
        r["ChartGridColor"] = Color.FromArgb(0x14, theme.Tick.R, theme.Tick.G, theme.Tick.B);
        r["LogoGlowColor"] = theme.Cpu;
        r["GlowOpacity"] = theme.GlowOpacity;
        r["GaugeDimAlpha"] = theme.DimAlpha;

        r["TitleFont"] = Font(theme.TitleFont);
        r["BodyFont"] = Font(theme.BodyFont);
        r["TitleBrandText"] = theme.TrackedTitle ? "F S" : "FS";
        r["TitleRestText"] = theme.TrackedTitle ? "  H W  M O N I T O R" : " HW MONITOR";

        var logo = Logo(theme.LogoImage);
        r["LogoImageSource"] = logo;
        r["LogoImageVisibility"] = logo is null ? Visibility.Collapsed : Visibility.Visible;
        r["LogoRingVisibility"] = logo is null ? Visibility.Visible : Visibility.Collapsed;

        Changed?.Invoke();
    }

    /// <summary>"embedded:Poppins" means the copy bundled in Assets/Fonts; anything else is an installed font name.</summary>
    static FontFamily Font(string spec)
    {
        const string prefix = "embedded:";
        return spec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/Fonts/#{spec[prefix.Length..]}, Segoe UI")
            : new FontFamily($"{spec}, Segoe UI");
    }

    static ImageSource? Logo(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        try
        {
            var image = new BitmapImage(new Uri($"pack://application:,,,/Assets/{name}.png"));
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null; // unknown name in a custom theme: keep the ring logo
        }
    }

    static SolidColorBrush Solid(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    static LinearGradientBrush Vertical(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }

    static RadialGradientBrush Glow(Color c, Point center, double radiusX, double radiusY)
    {
        var brush = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B))
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
        };
        brush.Freeze();
        return brush;
    }

    static LinearGradientBrush Spectrum(AppTheme t)
    {
        if (t is { BrandPrimary: Color primary, BrandSecondary: Color secondary })
        {
            // Hard split: F in one color, S in the other.
            var split = new LinearGradientBrush(
                [
                    new GradientStop(primary, 0),
                    new GradientStop(primary, 0.5),
                    new GradientStop(secondary, 0.5),
                    new GradientStop(secondary, 1),
                ],
                new Point(0, 0.5), new Point(1, 0.5));
            split.Freeze();
            return split;
        }

        var brush = new LinearGradientBrush(
            [
                new GradientStop(t.Cpu, 0),
                new GradientStop(t.Gpu, 0.33),
                new GradientStop(t.Ram, 0.66),
                new GradientStop(t.Ssd, 1),
            ],
            new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }
}
