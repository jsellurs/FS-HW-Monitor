using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FSHWMonitor.Core;
using FSHWMonitor.Platform;
using LibreHardwareMonitor.Hardware;

namespace FSHWMonitor.Sensors;

/// <summary>
/// Reads real hardware through LibreHardwareMonitorLib. CPU, DIMM (SPD), NVMe and fan sensors need
/// administrator rights; CPU, DIMM and motherboard sensors also need the PawnIO kernel driver.
/// </summary>
public sealed partial class LhmSensorSource : ISensorSource
{
    static readonly HardwareType[] CoolingHardware = [HardwareType.SuperIO, HardwareType.Cooler, HardwareType.EmbeddedController];

    readonly Computer _computer;
    readonly object _gate = new();
    bool _open;
    bool _disposed;

    /// <param name="readUsbCoolers">Also poll USB AIO coolers and fan hubs (see <see cref="AppSettings.ReadUsbCoolers"/>).</param>
    public LhmSensorSource(bool readUsbCoolers = false)
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,           // fan headers and board temperatures (Super I/O chip)
            IsControllerEnabled = readUsbCoolers,  // AIO coolers and fan hubs (Corsair, NZXT, Aquacomputer...)
        };
    }

    public void Open()
    {
        lock (_gate)
        {
            if (_open || _disposed) return;
            _computer.Open();
            _open = true;
        }
    }

    public HardwareSnapshot Read(SensorPreferences preferences)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            foreach (var hw in _computer.Hardware)
                UpdateTree(hw);

            var all = Flatten(_computer.Hardware).ToList();
            return new HardwareSnapshot
            {
                Cards = new Dictionary<ComponentKind, ComponentReading>
                {
                    [ComponentKind.Cpu] = ReadCpu(all, preferences.GaugeSensor(ComponentKind.Cpu)),
                    [ComponentKind.Gpu] = ReadGpu(all, preferences.GaugeSensor(ComponentKind.Gpu)),
                    [ComponentKind.Ram] = ReadRam(all, preferences.GaugeSensor(ComponentKind.Ram)),
                    [ComponentKind.Ssd] = ReadSsd(all, preferences.GaugeSensor(ComponentKind.Ssd)),
                    [ComponentKind.Cooling] = ReadCooling(all, preferences),
                },
            };
        }
    }

    public string BuildReport()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var sb = new StringBuilder();
            sb.AppendLine($"FS HW Monitor {AppInfo.Version} sensor report, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"Administrator: {Elevation.IsAdministrator}   PawnIO driver: {DriverHelper.IsPawnIoInstalled}");
            sb.AppendLine();

            foreach (var hw in _computer.Hardware)
            {
                UpdateTree(hw);
                AppendHardware(sb, hw, 0);
            }

            return sb.ToString();
        }
    }

    public IReadOnlyList<SensorChoice> GetTemperatureChoices(ComponentKind kind)
    {
        lock (_gate)
        {
            if (!_open || _disposed) return [];
            var all = Flatten(_computer.Hardware).ToList();

            IEnumerable<ISensor> sensors = kind switch
            {
                ComponentKind.Cpu => all.Where(h => h.HardwareType == HardwareType.Cpu).SelectMany(ValidTemps),
                ComponentKind.Gpu => all.Where(IsGpu).SelectMany(ValidTemps),
                ComponentKind.Ram => all.Where(h => h.HardwareType == HardwareType.Memory).SelectMany(ValidTemps).Where(IsDimmTemp),
                ComponentKind.Ssd => all.Where(h => h.HardwareType == HardwareType.Storage).Select(DriveTemp).OfType<ISensor>(),
                _ => all.Where(h => CoolingHardware.Contains(h.HardwareType)).SelectMany(ValidTemps),
            };

            return sensors.Select(s => new SensorChoice(s.Identifier.ToString(), ChoiceLabel(kind, s))).ToList();
        }
    }

    public IReadOnlyList<FanInfo> GetFans()
    {
        lock (_gate)
        {
            if (!_open || _disposed) return [];
            var sources = Flatten(_computer.Hardware).Where(h => CoolingHardware.Contains(h.HardwareType)).ToList();
            return sources
                .SelectMany(h => h.Sensors.Where(s => s.SensorType == SensorType.Fan)
                    .Select(s => new FanInfo(s.Identifier.ToString(), sources.Count > 1 ? $"{h.Name} · {s.Name}" : s.Name, Val(s))))
                .ToList();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_open) _computer.Close();
        }
    }

    // CPU

    static ComponentReading ReadCpu(List<IHardware> all, string? pick)
    {
        var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is null)
            return new ComponentReading { DeviceName = "No CPU detected" };

        var temps = ValidTemps(cpu);
        var auto = FindIn(temps, "Core (Tctl/Tdie)", "CPU Package", "Core (Tctl)", "Core (Tdie)", "Package", "Core Max", "Core Average")
                   ?? temps.OrderByDescending(s => s.Value).FirstOrDefault();

        var details = new List<Detail>();
        foreach (var ccd in temps.Where(s => s.Name.StartsWith("CCD", StringComparison.OrdinalIgnoreCase)).Take(2))
            details.Add(TempDetail(BeforeParen(ccd.Name), ccd));

        if (auto?.Name is "CPU Package" or "Package" && FindIn(temps, "Core Max") is { } coreMax)
            details.Add(TempDetail("Core Max", coreMax));

        if (Val(Find(cpu, SensorType.Load, "CPU Total")) is double load)
            details.Add(Detail.Value("Load", $"{load:0}%"));

        if (Val(Find(cpu, SensorType.Power, "Package", "CPU Package")) is double watts && watts > 0)
            details.Add(Detail.Value("Power", $"{watts:0} W"));

        if (CpuClockMhz(cpu) is double mhz && mhz > 0)
            details.Add(Detail.Value("Clock", $"{mhz / 1000:0.00} GHz"));

        var main = ApplyPick(auto, Picked(all, pick), details);
        return new ComponentReading
        {
            DeviceName = cpu.Name,
            Celsius = Val(main),
            SensorName = main is null ? "" : SensorLabel(main.Name),
            Details = details,
            Note = main is null ? MissingDriverNote() : null,
        };
    }

    static double? CpuClockMhz(IHardware cpu)
    {
        if (Val(Find(cpu, SensorType.Clock, "Cores (Average)")) is double avg && avg > 0)
            return avg;

        var cores = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock && s.Value is > 0
                        && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Average", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double)s.Value!.Value)
            .ToList();
        return cores.Count > 0 ? cores.Average() : null;
    }

    // GPU

    static bool IsGpu(IHardware h) => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel;

    static ComponentReading ReadGpu(List<IHardware> all, string? pick)
    {
        // Prefer the discrete card: the one with a core temperature and the most dedicated memory.
        var gpu = all.Where(IsGpu).OrderByDescending(GpuScore).FirstOrDefault();
        if (gpu is null)
            return new ComponentReading { DeviceName = "No GPU detected" };

        var temps = ValidTemps(gpu);
        var auto = FindIn(temps, "GPU Core", "GPU Temperature", "GPU")
                   ?? temps.FirstOrDefault(s => !s.Name.Contains("VR", StringComparison.OrdinalIgnoreCase))
                   ?? temps.FirstOrDefault();

        var details = new List<Detail>();
        if (FindIn(temps, "GPU Hot Spot", "GPU Hotspot") is { } hotSpot)
            details.Add(TempDetail("Hot Spot", hotSpot));

        if (FindIn(temps, "GPU Memory Junction", "GPU Memory") is { } vram)
            details.Add(TempDetail("VRAM", vram));

        if (Val(Find(gpu, SensorType.Load, "GPU Core", "D3D 3D")) is double load)
            details.Add(Detail.Value("Load", $"{load:0}%"));

        if (Val(Find(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power", "GPU Core")) is double watts && watts > 0)
            details.Add(Detail.Value("Power", $"{watts:0} W"));

        if (Val(gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Value.HasValue)) is double rpm)
            details.Add(Detail.Value("Fan", $"{rpm:0} RPM"));
        else if (Val(Find(gpu, SensorType.Clock, "GPU Core")) is double clock && clock > 0)
            details.Add(Detail.Value("Clock", $"{clock:0} MHz"));

        var main = ApplyPick(auto, Picked(all, pick), details);
        return new ComponentReading
        {
            DeviceName = gpu.Name,
            Celsius = Val(main),
            SensorName = main is null ? "" : SensorLabel(main.Name),
            Details = details.Take(5).ToList(),
            Note = main is null ? "No GPU temperature sensor found" : null,
        };
    }

    static double GpuScore(IHardware gpu)
    {
        double score = Val(Find(gpu, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")) ?? 0;
        if (gpu.Sensors.Any(s => s.SensorType == SensorType.Temperature && s.Name == "GPU Core" && IsValidTemp(s)))
            score += 1_000_000;
        return score;
    }

    // RAM

    static bool IsDimmTemp(ISensor s) => s.Name.StartsWith("DIMM", StringComparison.OrdinalIgnoreCase);

    static ComponentReading ReadRam(List<IHardware> all, string? pick)
    {
        var dimms = all
            .Where(h => h.HardwareType == HardwareType.Memory)
            .Select(h => (Hardware: h, Sensor: ValidTemps(h).FirstOrDefault(IsDimmTemp)))
            .Where(d => d.Sensor != null)
            .Select(d => (d.Hardware, Sensor: d.Sensor!))
            .ToList();

        var total = all.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Identifier.ToString() == "/ram");
        double? used = Val(Find(total, SensorType.Data, "Memory Used"));
        double? available = Val(Find(total, SensorType.Data, "Memory Available"));

        var details = new List<Detail>();
        foreach (var dimm in dimms.Take(4))
            details.Add(TempDetail(DimmLabel(dimm.Sensor.Name), dimm.Sensor));

        if (used is double u && available is double a)
            details.Add(Detail.Value("Used", $"{u:0.0} / {u + a:0.0} GB"));
        if (dimms.Count == 0 && Val(Find(total, SensorType.Load, "Memory")) is double load)
            details.Add(Detail.Value("Load", $"{load:0}%"));

        var picked = Picked(all, pick);
        var main = picked ?? (dimms.Count > 0 ? dimms.MaxBy(d => d.Sensor.Value!.Value).Sensor : null);
        string? deviceName = dimms.Count > 0
            ? $"{DimmModel(dimms[0].Hardware.Name)} ×{dimms.Count}"
            : total != null ? "System memory" : "No memory detected";

        return new ComponentReading
        {
            DeviceName = deviceName,
            Celsius = Val(main),
            SensorName = picked != null ? DimmLabel(picked.Name) : "Hottest DIMM",
            Details = details,
            Note = main is null
                ? MissingDriverNote() ?? "No DIMM temperature sensors (common on DDR4)"
                : null,
        };
    }

    /// <summary>Turns "Team Group Inc - UD5-6000 (#0)" into "Team Group UD5-6000".</summary>
    static string DimmModel(string hardwareName)
    {
        string name = TrailingIndex().Replace(hardwareName, "");
        return VendorSeparator().Replace(name, " ").Trim();
    }

    static string DimmLabel(string sensorName)
    {
        // LibreHardwareMonitor numbers DIMMs from #0; show them from 1 like most BIOS screens.
        int hash = sensorName.IndexOf('#');
        return hash >= 0 && int.TryParse(sensorName[(hash + 1)..], out int n) ? $"DIMM {n + 1}" : sensorName;
    }

    // SSD

    static ComponentReading ReadSsd(List<IHardware> all, string? pick)
    {
        var storage = all.Where(h => h.HardwareType == HardwareType.Storage).ToList();
        var drives = storage
            .Select(h => (Hardware: h, Sensor: DriveTemp(h)))
            .Where(d => d.Sensor != null)
            .Select(d => (d.Hardware, Sensor: d.Sensor!))
            .OrderByDescending(d => d.Sensor.Value!.Value)
            .ToList();

        if (drives.Count == 0)
        {
            return new ComponentReading
            {
                DeviceName = storage.FirstOrDefault()?.Name ?? "No drive sensors",
                SensorName = "Composite",
                Note = Elevation.IsAdministrator ? "No drive temperature sensors found" : "Run as administrator to read drive sensors",
            };
        }

        // The user's pick decides which drive the card is about; otherwise the hottest one.
        var picked = Picked(all, pick);
        var (drive, temp) = picked != null && storage.FirstOrDefault(h => h.Sensors.Contains(picked)) is { } owner
            ? (owner, picked)
            : drives[0];

        var details = new List<Detail>();
        foreach (var other in drives.Where(d => d.Hardware != drive).Take(2))
            details.Add(TempDetail(ShortDriveName(other.Hardware.Name), other.Sensor));

        if (Val(Find(drive, SensorType.Level, "Remaining Life", "Life")) is double life)
            details.Add(Detail.Value("Life", $"{life:0}%"));

        double? totalGb = Val(Find(drive, SensorType.Data, "Total Space"));
        double? freeGb = Val(Find(drive, SensorType.Data, "Free Space"));
        if (totalGb is double t && freeGb is double f && t > 0)
            details.Add(Detail.Value("Used", FormatSpace(t - f, t)));

        if (Val(Find(drive, SensorType.Throughput, "Read Rate")) is double read)
            details.Add(Detail.Value("Read", UsageFormat.Rate(read)));
        if (Val(Find(drive, SensorType.Throughput, "Write Rate")) is double write)
            details.Add(Detail.Value("Write", UsageFormat.Rate(write)));

        string name = drive.Name;
        if (totalGb is double size && size > 0)
            name += size >= 1000 ? $" · {size / 1000:0.#} TB" : $" · {size:0} GB";
        if (drives.Count > 1)
            name += $" +{drives.Count - 1}";

        return new ComponentReading
        {
            DeviceName = name,
            Celsius = Val(temp),
            SensorName = drives.Count > 1 && picked is null ? "Hottest drive" : SensorLabel(temp.Name),
            Details = details.Take(5).ToList(),
        };
    }

    static ISensor? DriveTemp(IHardware drive) => drive.Sensors
        .Where(s => s.SensorType == SensorType.Temperature && IsValidTemp(s) && !DriveLimitName().IsMatch(s.Name))
        .OrderBy(s => s.Name switch { "Composite Temperature" => 0, "Temperature" => 1, _ => 2 })
        .FirstOrDefault();

    static string ShortDriveName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 2 ? string.Join(' ', words.Take(2)) : name;
    }

    static string FormatSpace(double usedGb, double totalGb) => totalGb >= 1000
        ? $"{usedGb / 1000:0.00} / {totalGb / 1000:0.00} TB"
        : $"{usedGb:0} / {totalGb:0} GB";

    // Cooling

    static ComponentReading ReadCooling(List<IHardware> all, SensorPreferences preferences)
    {
        var sources = all.Where(h => CoolingHardware.Contains(h.HardwareType)).ToList();
        var temps = sources.SelectMany(ValidTemps).ToList();

        // Gauge: the user's pick, else AIO liquid, else a sensor the board names as motherboard/system.
        // Unnamed Super I/O inputs ("Temperature #3") could be anything, so those are left for the user to pick.
        var auto = temps.FirstOrDefault(s => LiquidName().IsMatch(s.Name))
                   ?? temps.FirstOrDefault(s => BoardName().IsMatch(s.Name));
        var main = Picked(all, preferences.GaugeSensor(ComponentKind.Cooling)) ?? auto;

        // Fans: spinning ones plus any the user named; hidden ones never. At most five, busiest first if trimmed.
        var fans = sources
            .SelectMany(h => h.Sensors.Where(s => s.SensorType == SensorType.Fan && s.Value.HasValue))
            .Select((s, index) => (Sensor: s, Index: index, Pref: preferences.Fans.GetValueOrDefault(s.Identifier.ToString())))
            .Where(f => f.Pref?.Hidden != true && (f.Sensor.Value > 0 || !string.IsNullOrWhiteSpace(f.Pref?.Label)))
            .ToList();
        if (fans.Count > 5)
            fans = fans.OrderByDescending(f => f.Sensor.Value).Take(5).OrderBy(f => f.Index).ToList();

        var details = fans
            .Select(f => Detail.Value(string.IsNullOrWhiteSpace(f.Pref?.Label) ? f.Sensor.Name : f.Pref!.Label!.Trim(),
                $"{f.Sensor.Value!.Value.ToString("N0", CultureInfo.CurrentCulture)} RPM"))
            .ToList();

        string deviceName = sources.FirstOrDefault(h => h.HardwareType == HardwareType.Cooler)?.Name
                            ?? all.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard)?.Name
                            ?? "Fans & cooling";

        string? note = null;
        if (sources.Count == 0 || (fans.Count == 0 && main is null))
            note = MissingDriverNote() ?? "No fan or cooling sensors found";
        else if (main is null)
            note = "No liquid sensor found. Pick a temperature in Settings > Cooling";

        return new ComponentReading
        {
            DeviceName = deviceName,
            Celsius = Val(main),
            SensorName = main is null ? "" : LiquidName().IsMatch(main.Name) ? "Liquid" : SensorLabel(main.Name),
            Details = details,
            Note = note,
        };
    }

    // Helpers

    /// <summary>The sensor the user picked for a gauge, if it still exists and reads sensibly.</summary>
    static ISensor? Picked(List<IHardware> all, string? id) => id is null
        ? null
        : all.SelectMany(h => h.Sensors).FirstOrDefault(s =>
            s.SensorType == SensorType.Temperature && s.Identifier.ToString() == id && IsValidTemp(s));

    /// <summary>
    /// Puts the picked sensor on the gauge. If it was one of the detail rows (say GPU Hot Spot),
    /// the automatic choice takes over that row so nothing disappears.
    /// </summary>
    static ISensor? ApplyPick(ISensor? auto, ISensor? picked, List<Detail> details)
    {
        if (picked is null || picked == auto)
            return auto;

        int row = details.FindIndex(d => d.SensorId == picked.Identifier.ToString());
        if (row >= 0)
        {
            if (auto != null)
                details[row] = TempDetail(SensorLabel(auto.Name), auto);
            else
                details.RemoveAt(row);
        }
        return picked;
    }

    static Detail TempDetail(string label, ISensor sensor) => Detail.Temp(label, sensor.Value!.Value, sensor.Identifier.ToString());

    static string ChoiceLabel(ComponentKind kind, ISensor s)
    {
        string value = $"{s.Value:0}°C";
        return kind switch
        {
            ComponentKind.Cpu => $"{s.Name}  ({value})",
            ComponentKind.Ram => $"{DimmLabel(s.Name)}  ({value})",
            ComponentKind.Ssd => $"{s.Hardware.Name}  ({value})",
            _ => $"{s.Hardware.Name} · {s.Name}  ({value})",
        };
    }

    static string? MissingDriverNote()
    {
        if (!Elevation.IsAdministrator) return "Run as administrator to read this sensor";
        if (!DriverHelper.IsPawnIoInstalled) return "PawnIO driver not installed (right-click to install)";
        return null;
    }

    static void UpdateTree(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            UpdateTree(sub);
    }

    static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> list)
    {
        foreach (var hw in list)
        {
            yield return hw;
            foreach (var sub in Flatten(hw.SubHardware))
                yield return sub;
        }
    }

    static bool IsValidTemp(ISensor s) => s.Value is float v && v > 0.5f && v < 150f;

    static List<ISensor> ValidTemps(IHardware hw) =>
        hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && IsValidTemp(s)).ToList();

    static ISensor? Find(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw is null) return null;
        foreach (string name in names)
        {
            var match = hw.Sensors.FirstOrDefault(s =>
                s.SensorType == type && s.Value.HasValue && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    static ISensor? FindIn(List<ISensor> sensors, params string[] names)
    {
        foreach (string name in names)
        {
            var match = sensors.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    static double? Val(ISensor? s) => s?.Value is float v && !float.IsNaN(v) ? v : null;

    /// <summary>Turns "Core (Tctl/Tdie)" into "Tctl/Tdie" and "Composite Temperature" into "Composite".</summary>
    static string SensorLabel(string name)
    {
        int open = name.IndexOf('(');
        int close = name.IndexOf(')');
        if (open >= 0 && close > open)
            return name[(open + 1)..close];
        return name.Replace(" Temperature", "", StringComparison.OrdinalIgnoreCase);
    }

    static string BeforeParen(string name)
    {
        int open = name.IndexOf('(');
        return open > 0 ? name[..open].Trim() : name;
    }

    static void AppendHardware(StringBuilder sb, IHardware hw, int depth)
    {
        string pad = new(' ', depth * 2);
        sb.AppendLine($"{pad}[{hw.HardwareType}] {hw.Name}  ({hw.Identifier})");
        foreach (var s in hw.Sensors)
        {
            string value = s.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "n/a";
            sb.AppendLine($"{pad}  {s.SensorType,-12} {s.Name,-40} {value,12}  ({s.Identifier})");
        }
        foreach (var sub in hw.SubHardware)
            AppendHardware(sb, sub, depth + 1);
    }

    [GeneratedRegex(@"\s*\(#\d+\)$")]
    private static partial Regex TrailingIndex();

    [GeneratedRegex(@"\s+(Inc\.?|Corp\.?|Co\.?,? Ltd\.?|Ltd\.?)?\s*-\s+", RegexOptions.IgnoreCase)]
    private static partial Regex VendorSeparator();

    [GeneratedRegex("Warning|Critical|Limit|Threshold", RegexOptions.IgnoreCase)]
    private static partial Regex DriveLimitName();

    [GeneratedRegex("Liquid|Coolant|Water", RegexOptions.IgnoreCase)]
    private static partial Regex LiquidName();

    [GeneratedRegex(@"Motherboard|System|\bMB\b", RegexOptions.IgnoreCase)]
    private static partial Regex BoardName();
}
