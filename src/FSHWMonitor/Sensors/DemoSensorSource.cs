using System.Globalization;
using FSHWMonitor.Core;

namespace FSHWMonitor.Sensors;

/// <summary>Plausible simulated readings for screenshots, UI work and machines without sensors.</summary>
public sealed class DemoSensorSource : ISensorSource
{
    static readonly (string Id, string Name, double BaseRpm)[] DemoFans =
    [
        ("/demo/fan/0", "Pump", 2980),
        ("/demo/fan/1", "CPU Fan", 1150),
        ("/demo/fan/2", "Top 1", 905),
        ("/demo/fan/3", "Top 2", 910),
        ("/demo/fan/4", "Rear", 860),
    ];

    readonly Random _random = new(9800);
    double _t;

    public void Open() { }

    public HardwareSnapshot Read(SensorPreferences preferences)
    {
        double t = _t++;
        double Wave(double period, double phase = 0) => Math.Sin((t / period + phase) * Math.PI * 2);
        double Noise(double amplitude) => (_random.NextDouble() - 0.5) * amplitude;

        double cpuLoad = Math.Clamp(38 + 28 * Wave(97) + 14 * Wave(23, 0.3) + Noise(10), 2, 100);
        double cpu = 47 + cpuLoad * 0.31 + Noise(1.4);
        double gpuLoad = Math.Clamp(62 + 34 * Wave(150, 0.2) + Noise(6), 0, 100);
        double gpu = 43 + gpuLoad * 0.24 + Noise(0.8);
        double ram = 44 + 3 * Wave(240, 0.1) + cpuLoad * 0.02;
        double ssd = 46 + 6 * Wave(180, 0.6) + Noise(0.6);
        double ssdWrite = Math.Max(0, 1.4e8 * Wave(45, 0.4) + Noise(4e6));
        double liquid = 31 + cpuLoad * 0.06 + 1.5 * Wave(300, 0.3);

        var fans = DemoFans
            .Where(f => preferences.Fans.GetValueOrDefault(f.Id)?.Hidden != true)
            .Select(f => Detail.Value(
                preferences.Fans.GetValueOrDefault(f.Id)?.Label is { Length: > 0 } label ? label : f.Name,
                $"{(f.BaseRpm + cpuLoad * (f.Name == "Pump" ? 0.5 : 4) + Noise(20)).ToString("N0", CultureInfo.CurrentCulture)} RPM"))
            .ToList();

        return new HardwareSnapshot
        {
            Cards = new Dictionary<ComponentKind, ComponentReading>
            {
                [ComponentKind.Cpu] = new()
                {
                    DeviceName = "AMD Ryzen 7 9800X3D",
                    Celsius = cpu,
                    SensorName = "Tctl/Tdie",
                    Details =
                    [
                        Detail.Temp("CCD1", cpu - 5.5),
                        Detail.Value("Load", $"{cpuLoad:0}%"),
                        Detail.Value("Power", $"{35 + cpuLoad * 0.9:0} W"),
                        Detail.Value("Clock", $"{4.9 + cpuLoad * 0.0035:0.00} GHz"),
                    ],
                },
                [ComponentKind.Gpu] = new()
                {
                    DeviceName = "AMD Radeon RX 9070 XT",
                    Celsius = gpu,
                    SensorName = "GPU Core",
                    Details =
                    [
                        Detail.Temp("Hot Spot", gpu + 13),
                        Detail.Temp("VRAM", gpu + 20),
                        Detail.Value("Load", $"{gpuLoad:0}%"),
                        Detail.Value("Power", $"{20 + gpuLoad * 2.6:0} W"),
                        Detail.Value("Fan", $"{(gpuLoad > 25 ? 700 + gpuLoad * 9 : 0):0} RPM"),
                    ],
                },
                [ComponentKind.Ram] = new()
                {
                    DeviceName = "Team Group UD5-6000 ×4",
                    Celsius = ram + 1.5,
                    SensorName = "Hottest DIMM",
                    Details =
                    [
                        Detail.Temp("DIMM 1", ram + 1.5),
                        Detail.Temp("DIMM 2", ram + 0.75),
                        Detail.Temp("DIMM 3", ram),
                        Detail.Temp("DIMM 4", ram - 2),
                        Detail.Value("Used", $"{28.4 + 2 * Wave(300):0.0} / 61.6 GB"),
                    ],
                },
                [ComponentKind.Ssd] = new()
                {
                    DeviceName = "TEAM TM8FPK002T · 2 TB",
                    Celsius = ssd,
                    SensorName = "Composite",
                    Details =
                    [
                        Detail.Value("Life", "99%"),
                        Detail.Value("Used", "1.25 / 2.05 TB"),
                        Detail.Value("Read", $"{Math.Abs(Noise(9)):0.0} MB/s"),
                        Detail.Value("Write", $"{ssdWrite / 1e6:0.0} MB/s"),
                    ],
                },
                [ComponentKind.Cooling] = new()
                {
                    DeviceName = "ASRock X870 Pro RS",
                    Celsius = liquid,
                    SensorName = "Liquid",
                    Details = fans.Take(5).ToList(),
                },
            },
        };
    }

    public string BuildReport() => "FS HW Monitor demo mode: no real sensors are read." + Environment.NewLine;

    public IReadOnlyList<SensorChoice> GetTemperatureChoices(ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => [new("/demo/cpu/tctl", "Core (Tctl/Tdie)  (62°C)"), new("/demo/cpu/ccd1", "CCD1 (Tdie)  (57°C)")],
        ComponentKind.Gpu => [new("/demo/gpu/core", "AMD Radeon RX 9070 XT · GPU Core  (58°C)"), new("/demo/gpu/hotspot", "AMD Radeon RX 9070 XT · GPU Hot Spot  (71°C)")],
        ComponentKind.Ram => [new("/demo/dimm/0", "DIMM 1  (46°C)"), new("/demo/dimm/1", "DIMM 2  (45°C)")],
        ComponentKind.Ssd => [new("/demo/nvme/0", "TEAM TM8FPK002T  (49°C)")],
        _ => [new("/demo/aio/liquid", "Demo AIO · Liquid  (33°C)"), new("/demo/board/1", "Nuvoton NCT6799D · Temperature #1  (41°C)")],
    };

    public IReadOnlyList<FanInfo> GetFans() => DemoFans.Select(f => new FanInfo(f.Id, f.Name, f.BaseRpm)).ToList();

    public void Dispose() { }
}
