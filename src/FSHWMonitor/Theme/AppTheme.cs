using System.Text.Json.Serialization;
using System.Windows.Media;
using FSHWMonitor.Core;

namespace FSHWMonitor.Theme;

/// <summary>
/// Every color the dashboard uses. Built-in themes live in <see cref="Themes"/>; users can add their own as
/// JSON files in the themes folder (see <see cref="ThemeStore"/>).
/// </summary>
public sealed class AppTheme
{
    public required string Name { get; init; }

    /// <summary>True for themes loaded from the user's themes folder.</summary>
    [JsonIgnore]
    public bool IsCustom { get; set; }

    // Backdrop: vertical gradient plus two soft radial glows (top-left, bottom-right).
    public Color BackdropTop { get; init; }
    public Color BackdropBottom { get; init; }
    public Color GlowA { get; init; }
    public Color GlowB { get; init; }

    // Surfaces.
    public Color CardTop { get; init; }
    public Color CardBottom { get; init; }
    public Color CardEdge { get; init; }
    public Color CardEdgeBottom { get; init; }
    public Color Inset { get; init; }
    public Color InsetBorder { get; init; }
    public Color Divider { get; init; }
    public Color Hairline { get; init; }
    public Color Chart { get; init; }
    public Color ChartBorder { get; init; }
    public Color Pill { get; init; }
    public Color PillBorder { get; init; }

    // Text.
    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color Muted { get; init; }

    // One accent per card.
    public Color Cpu { get; init; }
    public Color Gpu { get; init; }
    public Color Ram { get; init; }
    public Color Ssd { get; init; }
    public Color Cooling { get; init; }

    // Status colors.
    public Color Warm { get; init; }
    public Color Hot { get; init; }
    public Color HighStat { get; init; }
    public Color LowStat { get; init; }

    // Gauge details.
    public Color Tick { get; init; }
    public double GlowOpacity { get; init; } = 0.85;
    public double DimAlpha { get; init; } = 0.12;

    /// <summary>How strongly a card's border takes its accent color at rest; 0 is flat. Warm/hot always show.</summary>
    public double CardAccentEdge { get; init; } = 1;

    // Typography: an installed font name, or "embedded:<Family>" for the fonts bundled in Assets/Fonts.
    public string TitleFont { get; init; } = "Bahnschrift";
    public string BodyFont { get; init; } = "Bahnschrift";

    /// <summary>Letter-spaced "F S  H W  M O N I T O R" header (suits condensed fonts); false prints it plainly.</summary>
    public bool TrackedTitle { get; init; } = true;

    /// <summary>A bundled header mark (Assets/&lt;name&gt;.png) shown instead of the gauge-ring logo.</summary>
    public string? LogoImage { get; init; }

    /// <summary>When both are set, the "FS" in the title is split into these two colors instead of a blend.</summary>
    public Color? BrandPrimary { get; init; }
    public Color? BrandSecondary { get; init; }

    public Color AccentFor(ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => Cpu,
        ComponentKind.Gpu => Gpu,
        ComponentKind.Ram => Ram,
        ComponentKind.Ssd => Ssd,
        _ => Cooling,
    };
}

public static class Themes
{
    /// <summary>
    /// The Full Send brand, matched to fullsendwow.com: flat charcoal cards, blue F / muted red S,
    /// Poppins titles and PT Sans text. The default.
    /// </summary>
    public static readonly AppTheme FullSend = new()
    {
        Name = "Full Send",
        BackdropTop = C("#1B1D21"), BackdropBottom = C("#1B1D21"), GlowA = C("#00000000"), GlowB = C("#00000000"),
        CardTop = C("#222429"), CardBottom = C("#222429"), CardEdge = C("#222429"), CardEdgeBottom = C("#222429"),
        Inset = C("#292C32"), InsetBorder = C("#292C32"), Divider = C("#383C44"), Hairline = C("#2C2F35"),
        Chart = C("#1E2024"), ChartBorder = C("#1E2024"), Pill = C("#222429"), PillBorder = C("#2C2F35"),
        TextPrimary = C("#F2F4F7"), TextSecondary = C("#B9BFC9"), Muted = C("#8B929D"),
        Cpu = C("#5089FF"), Gpu = C("#5089FF"), Ram = C("#5089FF"), Ssd = C("#5089FF"), Cooling = C("#5089FF"),
        Warm = C("#E2B53E"), Hot = C("#F25C59"), HighStat = C("#F08A87"), LowStat = C("#8FB3FF"),
        Tick = C("#FFFFFF"), GlowOpacity = 0, DimAlpha = 0.16, CardAccentEdge = 0,
        TitleFont = "embedded:Poppins", BodyFont = "embedded:PT Sans", TrackedTitle = false,
        LogoImage = "fs-mark", BrandPrimary = C("#5089FF"), BrandSecondary = C("#D6625F"),
    };

    /// <summary>Deep navy with a different neon color per card.</summary>
    public static readonly AppTheme Neon = new()
    {
        Name = "Neon",
        BackdropTop = C("#080B11"), BackdropBottom = C("#05070A"), GlowA = C("#2238BDF8"), GlowB = C("#1CE879F9"),
        CardTop = C("#EE0F1620"), CardBottom = C("#F00A0F16"), CardEdge = C("#1C2533"), CardEdgeBottom = C("#121923"),
        Inset = C("#0A1017"), InsetBorder = C("#151E2A"), Divider = C("#1A2330"), Hairline = C("#141C27"),
        Chart = C("#080D13"), ChartBorder = C("#131B26"), Pill = C("#CC0D131B"), PillBorder = C("#1C2532"),
        TextPrimary = C("#E8EEF6"), TextSecondary = C("#8C98AB"), Muted = C("#5B6678"),
        Cpu = C("#38BDF8"), Gpu = C("#34D399"), Ram = C("#A78BFA"), Ssd = C("#E879F9"), Cooling = C("#A3E635"),
        Warm = C("#FBBF24"), Hot = C("#F43F5E"), HighStat = C("#FB7185"), LowStat = C("#7DD3FC"),
        Tick = C("#FFFFFF"), GlowOpacity = 0.85, DimAlpha = 0.12,
    };

    /// <summary>Pure black and white; color only shows up when something runs warm or hot.</summary>
    public static readonly AppTheme Carbon = new()
    {
        Name = "Carbon",
        BackdropTop = C("#060606"), BackdropBottom = C("#000000"), GlowA = C("#0EFFFFFF"), GlowB = C("#00000000"),
        CardTop = C("#F20D0D0E"), CardBottom = C("#F5080809"), CardEdge = C("#26262A"), CardEdgeBottom = C("#18181B"),
        Inset = C("#09090B"), InsetBorder = C("#1F1F23"), Divider = C("#27272A"), Hairline = C("#18181B"),
        Chart = C("#070708"), ChartBorder = C("#1C1C1F"), Pill = C("#CC0D0D0E"), PillBorder = C("#27272A"),
        TextPrimary = C("#FAFAFA"), TextSecondary = C("#A1A1AA"), Muted = C("#63636B"),
        Cpu = C("#E4E4E7"), Gpu = C("#E4E4E7"), Ram = C("#E4E4E7"), Ssd = C("#E4E4E7"), Cooling = C("#E4E4E7"),
        Warm = C("#F59E0B"), Hot = C("#EF4444"), HighStat = C("#F4F4F5"), LowStat = C("#8B8B94"),
        Tick = C("#FFFFFF"), GlowOpacity = 0.3, DimAlpha = 0.1,
    };

    /// <summary>80s retro: purple night sky, hot pink and cyan.</summary>
    public static readonly AppTheme Synthwave = new()
    {
        Name = "Synthwave",
        BackdropTop = C("#1A0B33"), BackdropBottom = C("#0A0616"), GlowA = C("#33FF71CE"), GlowB = C("#2801CDFE"),
        CardTop = C("#E81C1036"), CardBottom = C("#F0110A24"), CardEdge = C("#3B2468"), CardEdgeBottom = C("#241640"),
        Inset = C("#150C2C"), InsetBorder = C("#2E1C54"), Divider = C("#3A2463"), Hairline = C("#26173F"),
        Chart = C("#120A24"), ChartBorder = C("#2A1A4A"), Pill = C("#CC1C1036"), PillBorder = C("#3B2468"),
        TextPrimary = C("#F8F0FF"), TextSecondary = C("#B8A6DA"), Muted = C("#7D6AA3"),
        Cpu = C("#01CDFE"), Gpu = C("#FF71CE"), Ram = C("#B967FF"), Ssd = C("#05FFA1"), Cooling = C("#FFF38A"),
        Warm = C("#FFD166"), Hot = C("#FF3D57"), HighStat = C("#FF71CE"), LowStat = C("#01CDFE"),
        Tick = C("#F0D9FF"), GlowOpacity = 1.0, DimAlpha = 0.14,
    };

    /// <summary>Light theme for white builds.</summary>
    public static readonly AppTheme Frost = new()
    {
        Name = "Frost",
        BackdropTop = C("#F1F5F9"), BackdropBottom = C("#E2E8F0"), GlowA = C("#4038BDF8"), GlowB = C("#30A78BFA"),
        CardTop = C("#F7FFFFFF"), CardBottom = C("#F2F8FAFC"), CardEdge = C("#CBD5E1"), CardEdgeBottom = C("#DCE3EC"),
        Inset = C("#F1F5F9"), InsetBorder = C("#E2E8F0"), Divider = C("#CBD5E1"), Hairline = C("#E2E8F0"),
        Chart = C("#F8FAFC"), ChartBorder = C("#E2E8F0"), Pill = C("#E6FFFFFF"), PillBorder = C("#CBD5E1"),
        TextPrimary = C("#0F172A"), TextSecondary = C("#475569"), Muted = C("#94A3B8"),
        Cpu = C("#0284C7"), Gpu = C("#059669"), Ram = C("#7C3AED"), Ssd = C("#C026D3"), Cooling = C("#65A30D"),
        Warm = C("#D97706"), Hot = C("#E11D48"), HighStat = C("#E11D48"), LowStat = C("#0284C7"),
        Tick = C("#0F172A"), GlowOpacity = 0.4, DimAlpha = 0.13,
    };

    /// <summary>Green phosphor CRT.</summary>
    public static readonly AppTheme Terminal = new()
    {
        Name = "Terminal",
        BackdropTop = C("#020805"), BackdropBottom = C("#000301"), GlowA = C("#1A22C55E"), GlowB = C("#0F22C55E"),
        CardTop = C("#F0041009"), CardBottom = C("#F5020A05"), CardEdge = C("#12452A"), CardEdgeBottom = C("#0B2818"),
        Inset = C("#03100A"), InsetBorder = C("#0F3320"), Divider = C("#134027"), Hairline = C("#0B2416"),
        Chart = C("#020B06"), ChartBorder = C("#0F2E1D"), Pill = C("#CC041009"), PillBorder = C("#12452A"),
        TextPrimary = C("#C8FFD8"), TextSecondary = C("#5CCB82"), Muted = C("#2F7A4C"),
        Cpu = C("#39FF7A"), Gpu = C("#39FF7A"), Ram = C("#39FF7A"), Ssd = C("#39FF7A"), Cooling = C("#39FF7A"),
        Warm = C("#FFD23F"), Hot = C("#FF4D4D"), HighStat = C("#B7FFCB"), LowStat = C("#1FA855"),
        Tick = C("#39FF7A"), GlowOpacity = 1.0, DimAlpha = 0.12,
    };

    public static IReadOnlyList<AppTheme> All { get; } = [FullSend, Neon, Carbon, Synthwave, Frost, Terminal];

    public static AppTheme Default => FullSend;

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
