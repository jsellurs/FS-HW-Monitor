using FSHWMonitor.Core;

namespace FSHWMonitor.Sensors;

/// <summary>One app in a "Most used" list. Processes with the same executable are added together.</summary>
/// <param name="Name">Friendly name (the exe's description when it has one).</param>
/// <param name="Path">Full path of one of its processes, for the icon; null when Windows won't say.</param>
/// <param name="Value">CPU/GPU percent, memory bytes or disk bytes per second.</param>
/// <param name="Count">How many processes were grouped.</param>
public sealed record ProcessUsage(string Name, string? Path, double Value, string ValueText, int Count);

/// <summary>Top five apps per resource.</summary>
public sealed class UsageSnapshot
{
    public static readonly UsageSnapshot Empty = new();

    public IReadOnlyList<ProcessUsage> Cpu { get; init; } = [];
    public IReadOnlyList<ProcessUsage> Gpu { get; init; } = [];
    public IReadOnlyList<ProcessUsage> Ram { get; init; } = [];
    public IReadOnlyList<ProcessUsage> Disk { get; init; } = [];

    public IReadOnlyList<ProcessUsage> For(ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => Cpu,
        ComponentKind.Gpu => Gpu,
        ComponentKind.Ram => Ram,
        ComponentKind.Ssd => Disk,
        _ => [],
    };
}

public interface IProcessUsageSource : IDisposable
{
    /// <summary>Measures since the previous call (rates and percentages need two samples).</summary>
    UsageSnapshot Sample();
}

/// <summary>Made-up but plausible top apps for demo mode and screenshots.</summary>
public sealed class DemoProcessUsage : IProcessUsageSource
{
    readonly Random _random = new(77);

    public UsageSnapshot Sample()
    {
        double N(double value, double spread) => Math.Max(0, value + (_random.NextDouble() - 0.5) * spread);

        return new UsageSnapshot
        {
            Cpu = Top(UsageFormat.Percent, ("World of Warcraft", N(16, 4), 1), ("Google Chrome", N(6, 3), 14), ("OBS Studio", N(4, 1), 2), ("Discord", N(1.6, 0.6), 6), ("Desktop Window Manager", N(1.1, 0.4), 1)),
            Gpu = Top(UsageFormat.Percent, ("World of Warcraft", N(82, 8), 1), ("OBS Studio", N(7, 2), 2), ("Desktop Window Manager", N(3, 1), 1), ("Google Chrome", N(1.5, 0.8), 14), ("FS HW Monitor", N(0.8, 0.2), 1)),
            Ram = Top(UsageFormat.Bytes, ("World of Warcraft", 6.2e9, 1), ("Google Chrome", 3.1e9, 14), ("Discord", 7.2e8, 6), ("OBS Studio", 5.6e8, 2), ("Battle.net", 4.1e8, 4)),
            Disk = Top(UsageFormat.Rate, ("Battle.net", N(4.2e7, 1e7), 4), ("World of Warcraft", N(1.2e7, 4e6), 1), ("Google Chrome", N(8e5, 3e5), 14), ("Windows Defender", N(3e5, 1e5), 1), ("OBS Studio", N(2e5, 5e4), 2)),
        };
    }

    static IReadOnlyList<ProcessUsage> Top(Func<double, string> format, params (string Name, double Value, int Count)[] apps) =>
        apps.OrderByDescending(a => a.Value).Select(a => new ProcessUsage(a.Name, null, a.Value, format(a.Value), a.Count)).ToList();

    public void Dispose() { }
}

public static class UsageFormat
{
    public static string Percent(double percent) => percent < 10 ? $"{percent:0.0}%" : $"{percent:0}%";

    public static string Bytes(double bytes) => bytes >= 1e9 ? $"{bytes / 1e9:0.00} GB" : $"{bytes / 1e6:0} MB";

    public static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1e9 => $"{bytesPerSecond / 1e9:0.00} GB/s",
        >= 1e6 => $"{bytesPerSecond / 1e6:0.0} MB/s",
        _ => $"{bytesPerSecond / 1e3:0} KB/s",
    };
}
