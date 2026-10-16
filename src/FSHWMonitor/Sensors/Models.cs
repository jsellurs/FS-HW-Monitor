using FSHWMonitor.Core;

namespace FSHWMonitor.Sensors;

/// <summary>One secondary row on a card. Temperatures are passed as <see cref="Celsius"/> so the UI can convert units.</summary>
public sealed record Detail(string Label, string? Text, double? Celsius = null, string? SensorId = null)
{
    public static Detail Temp(string label, double celsius, string? sensorId = null) => new(label, null, celsius, sensorId);
    public static Detail Value(string label, string text) => new(label, text);
}

/// <summary>What one dashboard card shows for a single poll.</summary>
public sealed class ComponentReading
{
    public static readonly ComponentReading Empty = new();

    public string? DeviceName { get; init; }

    /// <summary>The headline temperature shown on the gauge; null when unavailable.</summary>
    public double? Celsius { get; init; }

    /// <summary>Which sensor the headline temperature came from, e.g. "Tctl/Tdie".</summary>
    public string SensorName { get; init; } = "";

    public IReadOnlyList<Detail> Details { get; init; } = [];

    /// <summary>Explains a missing reading, e.g. "Run as administrator".</summary>
    public string? Note { get; init; }
}

public sealed class HardwareSnapshot
{
    public required IReadOnlyDictionary<ComponentKind, ComponentReading> Cards { get; init; }

    public ComponentReading this[ComponentKind kind] => Cards.TryGetValue(kind, out var reading) ? reading : ComponentReading.Empty;
}

/// <summary>A temperature sensor the user can pick for a card's gauge.</summary>
public sealed record SensorChoice(string Id, string Label);

/// <summary>A fan or pump header as found on the system.</summary>
public sealed record FanInfo(string Id, string Name, double? Rpm);

/// <summary>The user's sensor picks, handed to the polling thread as an immutable snapshot.</summary>
public sealed class SensorPreferences
{
    public static readonly SensorPreferences Default = new();

    public IReadOnlyDictionary<ComponentKind, string> GaugeSensors { get; init; } = new Dictionary<ComponentKind, string>();
    public IReadOnlyDictionary<string, FanSettings> Fans { get; init; } = new Dictionary<string, FanSettings>();

    public string? GaugeSensor(ComponentKind kind) => GaugeSensors.TryGetValue(kind, out var id) ? id : null;

    public static SensorPreferences From(AppSettings settings) => new()
    {
        GaugeSensors = settings.Cards.Where(c => !string.IsNullOrEmpty(c.Sensor)).ToDictionary(c => c.Kind, c => c.Sensor!),
        Fans = settings.Fans.ToDictionary(f => f.Key, f => new FanSettings { Label = f.Value.Label, Hidden = f.Value.Hidden }),
    };
}

public interface ISensorSource : IDisposable
{
    /// <summary>Detects hardware. Can take a few seconds; call off the UI thread.</summary>
    void Open();

    HardwareSnapshot Read(SensorPreferences preferences);

    /// <summary>Plain-text listing of every sensor, for troubleshooting.</summary>
    string BuildReport();

    /// <summary>Temperatures that could drive the given card's gauge.</summary>
    IReadOnlyList<SensorChoice> GetTemperatureChoices(ComponentKind kind);

    /// <summary>Every fan and pump header the system reports.</summary>
    IReadOnlyList<FanInfo> GetFans();
}
