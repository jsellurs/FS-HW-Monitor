using System.IO;

namespace FSHWMonitor.Core;

/// <summary>Command-line switches (see README for the full list).</summary>
public sealed class StartupOptions
{
    /// <summary>Simulated sensor data; no admin rights or driver needed.</summary>
    public bool Demo { get; private set; }

    /// <summary>Normal resizable window instead of covering the dedicated display.</summary>
    public bool Windowed { get; private set; }

    /// <summary>Skip the automatic relaunch as administrator.</summary>
    public bool NoElevate { get; private set; }

    /// <summary>Render the dashboard to a PNG after a short delay, then exit.</summary>
    public string? SnapshotPath { get; private set; }

    public int SnapshotDelaySeconds { get; private set; } = 6;

    /// <summary>With --snapshot: capture the Settings window instead of the dashboard (for docs).</summary>
    public bool SnapshotSettings { get; private set; }

    /// <summary>Which Settings tab to capture, by its header (e.g. "Cards").</summary>
    public string? SnapshotSettingsTab { get; private set; }

    /// <summary>With --snapshot: capture a card's "Most used" popup instead of the dashboard (for docs).</summary>
    public ComponentKind? SnapshotUsage { get; private set; }

    /// <summary>Cards to show for this run, in order (e.g. cpu,gpu,cooling). Not saved.</summary>
    public IReadOnlyList<ComponentKind>? Cards { get; private set; }

    /// <summary>Write every detected sensor to a text file, then exit.</summary>
    public string? DumpPath { get; private set; }

    /// <summary>Closing hides to the tray for this run, whatever the setting says.</summary>
    public bool CloseToTray { get; private set; }

    /// <summary>Render on the CPU instead of the GPU (for graphics driver trouble).</summary>
    public bool SoftwareRendering { get; private set; }

    /// <summary>Theme name for this run, overriding settings.</summary>
    public string? Theme { get; private set; }

    /// <summary>Layout for this run (auto, landscape, portrait), overriding settings.</summary>
    public LayoutMode? Layout { get; private set; }

    /// <summary>Display device name (e.g. DISPLAY2) to cover, overriding settings.</summary>
    public string? Display { get; private set; }

    /// <summary>Arguments with file paths made absolute, safe to pass to an elevated relaunch.</summary>
    public IReadOnlyList<string> NormalizedArgs { get; private set; } = [];

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var o = new StartupOptions();
        var normalized = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string? NextValue() => i + 1 < args.Count && !args[i + 1].StartsWith("--") ? args[++i] : null;

            switch (arg.ToLowerInvariant())
            {
                case "--demo":
                    o.Demo = true;
                    break;
                case "--windowed":
                    o.Windowed = true;
                    break;
                case "--no-elevate":
                    o.NoElevate = true;
                    break;
                case "--software-render":
                    o.SoftwareRendering = true;
                    break;
                case "--close-to-tray":
                    o.CloseToTray = true;
                    break;
                case "--snapshot-settings":
                    o.SnapshotSettings = true;
                    o.SnapshotSettingsTab = NextValue();
                    normalized.Add(arg);
                    if (o.SnapshotSettingsTab != null) normalized.Add(o.SnapshotSettingsTab);
                    continue;
                case "--snapshot-usage":
                    string? usageCard = NextValue();
                    o.SnapshotUsage = Enum.TryParse<ComponentKind>(usageCard, ignoreCase: true, out var usageKind) ? usageKind : ComponentKind.Cpu;
                    normalized.Add(arg);
                    if (usageCard != null) normalized.Add(usageCard);
                    continue;
                case "--cards":
                    string? list = NextValue();
                    o.Cards = list?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(name => Enum.TryParse<ComponentKind>(name, ignoreCase: true, out var kind) ? kind : (ComponentKind?)null)
                        .OfType<ComponentKind>()
                        .Distinct()
                        .ToList();
                    normalized.Add(arg);
                    if (list != null) normalized.Add(list);
                    continue;
                case "--snapshot":
                    o.SnapshotPath = Path.GetFullPath(NextValue() ?? "fshw-snapshot.png");
                    normalized.Add(arg);
                    normalized.Add(o.SnapshotPath);
                    continue;
                case "--snapshot-delay":
                    if (int.TryParse(NextValue(), out int delay))
                        o.SnapshotDelaySeconds = Math.Clamp(delay, 1, 120);
                    normalized.Add(arg);
                    normalized.Add(o.SnapshotDelaySeconds.ToString());
                    continue;
                case "--dump":
                    o.DumpPath = Path.GetFullPath(NextValue() ?? "fshw-sensors.txt");
                    normalized.Add(arg);
                    normalized.Add(o.DumpPath);
                    continue;
                case "--display":
                    o.Display = NextValue();
                    normalized.Add(arg);
                    if (o.Display != null) normalized.Add(o.Display);
                    continue;
                case "--theme":
                    o.Theme = NextValue();
                    normalized.Add(arg);
                    if (o.Theme != null) normalized.Add(o.Theme);
                    continue;
                case "--layout":
                    string? layout = NextValue();
                    o.Layout = AppSettings.ParseLayout(layout);
                    normalized.Add(arg);
                    if (layout != null) normalized.Add(layout);
                    continue;
            }

            normalized.Add(arg);
        }

        o.NormalizedArgs = normalized;
        return o;
    }
}
