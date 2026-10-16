using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FSHWMonitor.Core;

/// <summary>
/// Landscape: four tall cards side by side (1280×800). Portrait: stacked rows (800×1280).
/// Bar / TallBar: compact cards for strip displays (1920×480, 480×1920). Grid: two columns for squarish screens.
/// </summary>
public enum LayoutMode { Auto, Landscape, Portrait, Bar, TallBar, Grid }

/// <summary>One dashboard card: whether it shows, which sensor drives its gauge, and its scale/alert points in °C.</summary>
public sealed class CardSettings
{
    public ComponentKind Kind { get; set; }
    public bool Visible { get; set; } = true;

    /// <summary>Sensor identifier picked in Settings; null chooses one automatically.</summary>
    public string? Sensor { get; set; }

    public double Min { get; set; }
    public double Max { get; set; }
    public double Warn { get; set; }
    public double Crit { get; set; }

    public bool IsValid => Max > Min && Crit >= Warn;

    public static CardSettings Default(ComponentKind kind) => kind switch
    {
        // Ryzen/Core CPUs throttle around 90-100 °C; GPU edge temps are comfortable below 80 °C.
        ComponentKind.Cpu => new() { Kind = kind, Min = 20, Max = 100, Warn = 80, Crit = 90 },
        ComponentKind.Gpu => new() { Kind = kind, Min = 20, Max = 100, Warn = 80, Crit = 90 },
        // DDR5 SPD hubs flag 55 °C as "high"; most NVMe drives throttle around 70-80 °C.
        ComponentKind.Ram => new() { Kind = kind, Min = 20, Max = 90, Warn = 60, Crit = 75 },
        ComponentKind.Ssd => new() { Kind = kind, Min = 20, Max = 90, Warn = 70, Crit = 80 },
        // AIO liquid usually sits at 30-40 °C and pumps are rated to ~60 °C; board sensors run 35-50 °C.
        _ => new() { Kind = kind, Visible = false, Min = 15, Max = 80, Warn = 50, Crit = 60 },
    };
}

/// <summary>Display name and visibility for one fan header, keyed by sensor identifier.</summary>
public sealed class FanSettings
{
    public string? Label { get; set; }
    public bool Hidden { get; set; }
}

/// <summary>Dims or blacks out the dashboard on a schedule (software dimming: the panel backlight stays on).</summary>
public sealed class NightSettings
{
    public bool Enabled { get; set; }
    public string Start { get; set; } = "23:00";
    public string End { get; set; } = "07:00";

    /// <summary>True blacks the screen out; false dims it to <see cref="Brightness"/>.</summary>
    public bool Black { get; set; }

    public double Brightness { get; set; } = 0.25;

    /// <summary>Light back up while anything runs hot.</summary>
    public bool WakeWhenHot { get; set; } = true;

    public bool IsNight(DateTime now)
    {
        if (!Enabled || !TryParseTime(Start, out var start) || !TryParseTime(End, out var end) || start == end)
            return false;
        var t = now.TimeOfDay;
        return start < end ? t >= start && t < end : t >= start || t < end; // the window may wrap past midnight
    }

    public static bool TryParseTime(string? text, out TimeSpan time) =>
        TimeSpan.TryParseExact(text?.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
}

/// <summary>User settings, stored as JSON in %APPDATA%\FS HW Monitor\settings.json.</summary>
public sealed class AppSettings
{
    public const string WindowedDisplay = "windowed";

    /// <summary>Display to cover (e.g. \\.\DISPLAY2), "windowed", or null to find the in-case screen automatically.</summary>
    public string? DisplayDevice { get; set; }

    public string Theme { get; set; } = "Full Send";

    /// <summary>A <see cref="LayoutMode"/> name; "Auto" picks from the display's shape.</summary>
    public string Layout { get; set; } = nameof(LayoutMode.Auto);

    public bool UseFahrenheit { get; set; }
    public bool AlwaysOnTop { get; set; }

    /// <summary>Closing the window hides it to the notification area instead of exiting.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>Set once the starter theme file has been copied to the themes folder.</summary>
    public bool StarterThemeInstalled { get; set; }
    public int PollIntervalMs { get; set; } = 1000;
    public int HistoryMinutes { get; set; } = 5;

    /// <summary>Every card, in display order.</summary>
    public List<CardSettings> Cards { get; set; } = ComponentKinds.All.Select(CardSettings.Default).ToList();

    public Dictionary<string, FanSettings> Fans { get; set; } = [];

    /// <summary>
    /// Also read USB AIO coolers and fan hubs. Off by default: polling them alongside vendor software
    /// (iCUE, NZXT CAM, Synapse) can make either app misbehave. Takes effect on restart.
    /// </summary>
    public bool ReadUsbCoolers { get; set; }

    public NightSettings Night { get; set; } = new();

    // Version 1.0 stored per-card limits here. Read once, folded into Cards, and never written again.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TempLimits? Cpu { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TempLimits? Gpu { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TempLimits? Ram { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TempLimits? Ssd { get; set; }

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FS HW Monitor");

    static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CardSettings Card(ComponentKind kind) => Cards.First(c => c.Kind == kind);

    public LayoutMode GetLayout() => ParseLayout(Layout) ?? LayoutMode.Auto;

    public static LayoutMode? ParseLayout(string? value) =>
        Enum.TryParse<LayoutMode>(value, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : null;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded != null)
                    return Sanitize(loaded);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read settings, using defaults: " + ex.Message);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save settings: " + ex.Message);
        }
    }

    static AppSettings Sanitize(AppSettings s)
    {
        s.Theme ??= "Full Send";
        s.Layout = (ParseLayout(s.Layout) ?? LayoutMode.Auto).ToString();
        s.PollIntervalMs = Math.Clamp(s.PollIntervalMs, 250, 10_000);
        s.HistoryMinutes = Math.Clamp(s.HistoryMinutes, 1, 60);
        s.Fans ??= [];
        s.Night ??= new NightSettings();
        s.Night.Brightness = Math.Clamp(s.Night.Brightness, 0.05, 0.9);

        // Exactly one entry per card kind, keeping the saved order.
        var cards = (s.Cards ?? []).Where(c => c != null && Enum.IsDefined(c.Kind)).DistinctBy(c => c.Kind).ToList();
        foreach (var kind in ComponentKinds.All.Where(k => cards.All(c => c.Kind != k)))
            cards.Add(CardSettings.Default(kind));
        foreach (var card in cards.Where(c => !c.IsValid))
        {
            var d = CardSettings.Default(card.Kind);
            (card.Min, card.Max, card.Warn, card.Crit) = (d.Min, d.Max, d.Warn, d.Crit);
        }
        if (cards.All(c => !c.Visible))
            cards[0].Visible = true;
        s.Cards = cards;

        // Fold 1.0-style limits into the cards.
        foreach (var (kind, legacy) in new[] { (ComponentKind.Cpu, s.Cpu), (ComponentKind.Gpu, s.Gpu), (ComponentKind.Ram, s.Ram), (ComponentKind.Ssd, s.Ssd) })
        {
            if (legacy is { } l && l.Max > l.Min && l.Crit >= l.Warn)
            {
                var card = s.Card(kind);
                (card.Min, card.Max, card.Warn, card.Crit) = (l.Min, l.Max, l.Warn, l.Crit);
            }
        }
        s.Cpu = s.Gpu = s.Ram = s.Ssd = null;

        return s;
    }
}

/// <summary>Version 1.0 settings shape, kept only to read old files.</summary>
public sealed class TempLimits
{
    public double Min { get; set; }
    public double Max { get; set; }
    public double Warn { get; set; }
    public double Crit { get; set; }
}
