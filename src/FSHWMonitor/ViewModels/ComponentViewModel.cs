using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using FSHWMonitor.Core;
using FSHWMonitor.Platform;
using FSHWMonitor.Sensors;
using FSHWMonitor.Theme;

namespace FSHWMonitor.ViewModels;

public enum TempLevel { NoData, Normal, Warm, Hot }

/// <summary>
/// One dashboard card. Readings and stats are kept in °C; everything shown is converted
/// to the current display unit in <see cref="Render"/>.
/// </summary>
public sealed class ComponentViewModel : ObservableObject
{
    readonly CardSettings _limits;
    readonly SessionStats _stats = new();
    ComponentReading? _last;
    bool _fahrenheit;

    /// <param name="card">Live settings for this card; edits made in Settings show up on the next <see cref="Refresh"/>.</param>
    public ComponentViewModel(CardSettings card, int historyCapacity, string historyLabel)
    {
        Kind = card.Kind;
        Title = card.Kind.Title();
        _limits = card;
        History = new HistoryBuffer(historyCapacity);
        HistoryLabel = historyLabel;
        ResetCommand = new RelayCommand(ResetStats);
        ApplyTheme();
    }

    public ComponentKind Kind { get; }
    public string Title { get; }
    public HistoryBuffer History { get; }
    public string HistoryLabel { get; }
    public ICommand ResetCommand { get; }
    public ObservableCollection<DetailItemViewModel> Details { get; } = [];
    public TempLevel Level { get; private set; }

    Color _accentColor;
    public Color AccentColor { get => _accentColor; private set => Set(ref _accentColor, value); }

    Brush _accentBrush = Brushes.Transparent;
    public Brush AccentBrush { get => _accentBrush; private set => Set(ref _accentBrush, value); }

    string _subtitle = "Detecting hardware…";
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }

    string _sensorName = "";
    public string SensorName { get => _sensorName; private set => Set(ref _sensorName, value); }

    string _valueText = "--";
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    string _unitText = "°C";
    public string UnitText { get => _unitText; private set => Set(ref _unitText, value); }

    double _gaugeValue = double.NaN;
    public double GaugeValue { get => _gaugeValue; private set => Set(ref _gaugeValue, value); }

    double _gaugeMin, _gaugeMax = 100, _gaugeWarn = 80, _gaugeCrit = 90;
    public double GaugeMin { get => _gaugeMin; private set => Set(ref _gaugeMin, value); }
    public double GaugeMax { get => _gaugeMax; private set => Set(ref _gaugeMax, value); }
    public double GaugeWarn { get => _gaugeWarn; private set => Set(ref _gaugeWarn, value); }
    public double GaugeCrit { get => _gaugeCrit; private set => Set(ref _gaugeCrit, value); }

    double _lowMarker = double.NaN, _highMarker = double.NaN;
    public double LowMarker { get => _lowMarker; private set => Set(ref _lowMarker, value); }
    public double HighMarker { get => _highMarker; private set => Set(ref _highMarker, value); }

    string _highText = "--", _avgText = "--", _lowText = "--";
    public string HighText { get => _highText; private set => Set(ref _highText, value); }
    public string AvgText { get => _avgText; private set => Set(ref _avgText, value); }
    public string LowText { get => _lowText; private set => Set(ref _lowText, value); }

    string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    Brush _statusBrush = Brushes.Transparent;
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    Brush _statusSoftBrush = Brushes.Transparent;
    public Brush StatusSoftBrush { get => _statusSoftBrush; private set => Set(ref _statusSoftBrush, value); }

    Brush _valueBrush = Brushes.Transparent;
    public Brush ValueBrush { get => _valueBrush; private set => Set(ref _valueBrush, value); }

    Brush _cardBorderBrush = Brushes.Transparent;
    public Brush CardBorderBrush { get => _cardBorderBrush; private set => Set(ref _cardBorderBrush, value); }

    string? _note;
    public string? Note { get => _note; private set { if (Set(ref _note, value)) HasNote = !string.IsNullOrEmpty(value); } }

    bool _hasNote;
    public bool HasNote { get => _hasNote; private set => Set(ref _hasNote, value); }

    int _historyVersion;
    public int HistoryVersion { get => _historyVersion; private set => Set(ref _historyVersion, value); }

    string _historyRangeText = "";
    public string HistoryRangeText { get => _historyRangeText; private set => Set(ref _historyRangeText, value); }

    public void Apply(ComponentReading reading)
    {
        _last = reading;
        if (reading.Celsius is double celsius)
        {
            _stats.Add(celsius);
            History.Add(celsius);
            HistoryVersion++;
        }
        Render();
    }

    // "Most used"

    public bool ShowsTopProcesses => Kind != ComponentKind.Cooling;

    public string TopProcessesTitle => Kind switch
    {
        ComponentKind.Cpu => "Top CPU users",
        ComponentKind.Gpu => "Top GPU users",
        ComponentKind.Ram => "Top memory users",
        _ => "Top disk users",
    };

    public string TopProcessesHint => Kind switch
    {
        ComponentKind.Cpu => "Share of total CPU, last 2 seconds",
        ComponentKind.Gpu => "Busiest GPU engine per app",
        ComponentKind.Ram => "Private memory in use",
        _ => "File reads + writes per second",
    };

    IReadOnlyList<TopProcessItem> _topProcesses = [];
    public IReadOnlyList<TopProcessItem> TopProcesses { get => _topProcesses; private set => Set(ref _topProcesses, value); }

    string _topProcessSummary = "Measuring…";
    public string TopProcessSummary { get => _topProcessSummary; private set => Set(ref _topProcessSummary, value); }

    bool _hasNoTopProcesses;
    public bool HasNoTopProcesses { get => _hasNoTopProcesses; private set => Set(ref _hasNoTopProcesses, value); }

    /// <summary>Explains the "×14" counts, using a real entry from the list; empty when nothing is grouped.</summary>
    string _countNote = "";
    public string CountNote { get => _countNote; private set { if (Set(ref _countNote, value)) HasCountNote = value.Length > 0; } }

    bool _hasCountNote;
    public bool HasCountNote { get => _hasCountNote; private set => Set(ref _hasCountNote, value); }

    public void ApplyUsage(IReadOnlyList<ProcessUsage> usage)
    {
        if (!ShowsTopProcesses)
            return;

        double max = usage.Count > 0 ? usage.Max(u => u.Value) : 0;
        TopProcesses = usage
            .Select(u => new TopProcessItem(
                u.Name,
                u.ValueText,
                u.Count > 1 ? $"×{u.Count}" : "",
                IconCache.Get(u.Path),
                u.Name.Length > 0 ? char.ToUpperInvariant(u.Name[0]).ToString() : "?",
                max > 0 ? Math.Max(0.03, u.Value / max) : 0,
                AccentBrush))
            .ToList();
        TopProcessSummary = usage.Count > 0 ? usage[0].Name : "Nothing busy";
        HasNoTopProcesses = usage.Count == 0;

        var grouped = usage.FirstOrDefault(u => u.Count > 1);
        CountNote = grouped is null
            ? ""
            : $"×{grouped.Count}: {grouped.Name} runs as {grouped.Count} processes, added together here.";
    }

    public void ApplyTheme()
    {
        AccentColor = ThemeManager.Current.AccentFor(Kind);
        AccentBrush = Palette.Brush(AccentColor);
        Render();
    }

    /// <summary>Re-renders after the card's limits changed in Settings.</summary>
    public void Refresh() => Render();

    public void SetFahrenheit(bool fahrenheit)
    {
        _fahrenheit = fahrenheit;
        Render();
    }

    public void ResetStats()
    {
        _stats.Reset();
        History.Clear();
        HistoryVersion++;
        Render();
    }

    double Display(double celsius) => _fahrenheit ? Units.ToFahrenheit(celsius) : celsius;

    string Format(double celsius) => Math.Round(Display(celsius)).ToString("0", CultureInfo.CurrentCulture);

    void Render()
    {
        var reading = _last;
        double? celsius = reading?.Celsius;
        bool hasStats = _stats.Count > 0;

        Subtitle = reading?.DeviceName ?? "Detecting hardware…";
        SensorName = reading?.SensorName ?? "";
        UnitText = _fahrenheit ? "°F" : "°C";

        GaugeMin = Display(_limits.Min);
        GaugeMax = Display(_limits.Max);
        GaugeWarn = Display(_limits.Warn);
        GaugeCrit = Display(_limits.Crit);
        GaugeValue = celsius.HasValue ? Display(celsius.Value) : double.NaN;
        ValueText = celsius.HasValue ? Format(celsius.Value) : "--";

        HighText = hasStats ? Format(_stats.Max) + "°" : "--";
        AvgText = hasStats ? Format(_stats.Average) + "°" : "--";
        LowText = hasStats ? Format(_stats.Min) + "°" : "--";
        HighMarker = hasStats ? Display(_stats.Max) : double.NaN;
        LowMarker = hasStats ? Display(_stats.Min) : double.NaN;

        Level = celsius switch
        {
            null => TempLevel.NoData,
            double t when t >= _limits.Crit => TempLevel.Hot,
            double t when t >= _limits.Warn => TempLevel.Warm,
            _ => TempLevel.Normal,
        };

        var theme = ThemeManager.Current;
        (string status, Color statusColor) = Level switch
        {
            TempLevel.Hot => ("HOT", theme.Hot),
            TempLevel.Warm => ("WARM", theme.Warm),
            TempLevel.Normal => ("NORMAL", AccentColor),
            _ => (reading is null ? "WAIT" : "NO DATA", theme.Muted),
        };
        StatusText = status;
        StatusBrush = Palette.Brush(statusColor);
        StatusSoftBrush = Palette.Brush(statusColor, 0x24);
        ValueBrush = Level switch
        {
            TempLevel.Hot => Palette.Brush(theme.Hot),
            TempLevel.Warm => Palette.Brush(theme.Warm),
            TempLevel.NoData => Palette.Brush(theme.Muted),
            _ => Palette.Brush(theme.TextPrimary),
        };
        bool alarm = Level is TempLevel.Warm or TempLevel.Hot;
        CardBorderBrush = Palette.CardBorder(alarm ? statusColor : AccentColor, alarm ? 1 : theme.CardAccentEdge);

        Note = reading?.Note;
        HistoryRangeText = HistoryRange();
        UpdateDetails(reading?.Details ?? []);
    }

    string HistoryRange()
    {
        if (History.Count == 0) return "";
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < History.Count; i++)
        {
            min = Math.Min(min, History[i]);
            max = Math.Max(max, History[i]);
        }
        return $"{Format(min)}–{Format(max)}°";
    }

    void UpdateDetails(IReadOnlyList<Detail> details)
    {
        // Update rows in place so the list doesn't flicker every poll.
        while (Details.Count > details.Count)
            Details.RemoveAt(Details.Count - 1);

        for (int i = 0; i < details.Count; i++)
        {
            var d = details[i];
            string text = d.Celsius is double c ? Format(c) + UnitText : d.Text ?? "--";
            if (i < Details.Count)
                Details[i].Update(d.Label, text);
            else
                Details.Add(new DetailItemViewModel(d.Label, text));
        }
    }
}

/// <summary>One row in a "Most used" popup.</summary>
/// <param name="Fraction">Bar length relative to the top entry, 0..1.</param>
public sealed record TopProcessItem(string Name, string ValueText, string CountText, ImageSource? Icon, string Initial, double Fraction, Brush Accent)
{
    public bool HasIcon => Icon != null;
}

public sealed class DetailItemViewModel(string label, string valueText) : ObservableObject
{
    string _label = label;
    public string Label { get => _label; private set => Set(ref _label, value); }

    string _valueText = valueText;
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    public void Update(string label, string valueText)
    {
        Label = label;
        ValueText = valueText;
    }
}
