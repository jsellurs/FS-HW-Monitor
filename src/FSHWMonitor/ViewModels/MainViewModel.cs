using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using FSHWMonitor.Core;
using FSHWMonitor.Platform;
using FSHWMonitor.Sensors;
using FSHWMonitor.Theme;

namespace FSHWMonitor.ViewModels;

/// <summary>Owns the sensor polling loop, the cards, and the header/footer state around them.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    static readonly TimeSpan UsageInterval = TimeSpan.FromSeconds(2);

    readonly AppSettings _settings;
    readonly ISensorSource _source;
    readonly IProcessUsageSource _usage;
    readonly bool _demo;
    readonly Dispatcher _dispatcher;
    readonly DispatcherTimer _clock;
    readonly Dictionary<ComponentKind, ComponentViewModel> _cards = [];
    CancellationTokenSource _pollCts = new();
    volatile SensorPreferences _preferences;
    DateTime _sessionStart = DateTime.Now;
    DateTime _lastErrorLog;
    DateTime _wakeUntil;
    DateTime _toastUntil;
    DateTime _lastUsageSample;
    bool _backlogLoaded;

    public MainViewModel(AppSettings settings, ISensorSource source, IProcessUsageSource usage, bool demo)
    {
        _settings = settings;
        _source = source;
        _usage = usage;
        _demo = demo;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _preferences = SensorPreferences.From(settings);

        CreateCards();
        ThemeManager.Changed += OnThemeChanged;

        Banner = demo ? "Demo mode · simulated data"
            : !Elevation.IsAdministrator ? "Limited mode · run as administrator for CPU, RAM, SSD & fans"
            : !DriverHelper.IsPawnIoInstalled ? "PawnIO driver missing · right-click to install"
            : null;

        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => OnClockTick();
        OnClockTick();
    }

    /// <summary>Raised after the set or order of visible cards changes, so the window can re-plan its layout.</summary>
    public event Action? CardsChanged;

    /// <summary>The visible cards, in display order.</summary>
    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    public ISensorSource Sensors => _source;

    public ComponentViewModel Card(ComponentKind kind) => _cards[kind];

    string _footerRight = "";
    public string FooterRight { get => _footerRight; private set => Set(ref _footerRight, value); }

    string _clockText = "";
    public string ClockText { get => _clockText; private set => Set(ref _clockText, value); }

    string _clockSuffix = "";
    public string ClockSuffix { get => _clockSuffix; private set => Set(ref _clockSuffix, value); }

    string _dateText = "";
    public string DateText { get => _dateText; private set => Set(ref _dateText, value); }

    string _sessionText = "";
    public string SessionText { get => _sessionText; private set => Set(ref _sessionText, value); }

    string _uptimeText = "";
    public string UptimeText { get => _uptimeText; private set => Set(ref _uptimeText, value); }

    string? _banner;
    public string? Banner { get => _banner; private set { if (Set(ref _banner, value)) HasBanner = !string.IsNullOrEmpty(value); } }

    bool _hasBanner;
    public bool HasBanner { get => _hasBanner; private set => Set(ref _hasBanner, value); }

    string? _toast;
    public string? Toast { get => _toast; private set { if (Set(ref _toast, value)) HasToast = !string.IsNullOrEmpty(value); } }

    bool _hasToast;
    public bool HasToast { get => _hasToast; private set => Set(ref _hasToast, value); }

    /// <summary>Opacity of the black night-mode overlay: 0 is off, 1 is fully black.</summary>
    double _dimOpacity;
    public double DimOpacity { get => _dimOpacity; private set => Set(ref _dimOpacity, value); }

    public void Start()
    {
        _clock.Start();
        _ = Task.Run(() => PollLoopAsync(_pollCts.Token));
    }

    public void SetFahrenheit(bool fahrenheit)
    {
        foreach (var card in _cards.Values)
            card.SetFahrenheit(fahrenheit);
    }

    public void ResetAllStats()
    {
        foreach (var card in _cards.Values)
            card.ResetStats();
        _sessionStart = DateTime.Now;
        OnClockTick();
    }

    /// <summary>Picks up card visibility, order, limits, gauge sensors and fan names from settings.</summary>
    public void ApplyCardSettings()
    {
        _preferences = SensorPreferences.From(_settings);
        foreach (var card in _cards.Values)
            card.Refresh();
        UpdateVisibleCards();
    }

    /// <summary>Restarts polling after the update interval or history length changed (stats start over).</summary>
    public void ApplyPollingSettings()
    {
        _pollCts.Cancel();
        _pollCts = new CancellationTokenSource();
        CreateCards();
        _ = Task.Run(() => PollLoopAsync(_pollCts.Token));
    }

    public void ApplyNightSettings() => UpdateNight();

    /// <summary>Lights the screen back up for a minute during night mode.</summary>
    public void WakeBriefly()
    {
        _wakeUntil = DateTime.Now.AddMinutes(1);
        UpdateNight();
    }

    public void ShowToast(string text, double seconds = 4)
    {
        Toast = text;
        _toastUntil = DateTime.Now.AddSeconds(seconds);
    }

    /// <summary>Builds a sensor report off the UI thread.</summary>
    public Task<string> BuildReportAsync() => Task.Run(_source.BuildReport);

    void CreateCards()
    {
        int capacity = Math.Max(2, _settings.HistoryMinutes * 60_000 / _settings.PollIntervalMs);
        string label = $"Last {_settings.HistoryMinutes} min";

        _cards.Clear();
        foreach (var card in _settings.Cards)
        {
            var vm = new ComponentViewModel(card, capacity, label);
            vm.SetFahrenheit(_settings.UseFahrenheit);
            _cards[card.Kind] = vm;
        }

        double hz = 1000.0 / _settings.PollIntervalMs;
        FooterRight = $"{hz.ToString("0.##", CultureInfo.InvariantCulture)} Hz  ·  right-click for menu";
        UpdateVisibleCards();
    }

    void UpdateVisibleCards()
    {
        Components.Clear();
        foreach (var card in _settings.Cards.Where(c => c.Visible))
            Components.Add(_cards[card.Kind]);
        CardsChanged?.Invoke();
    }

    async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            _source.Open();
        }
        catch (Exception ex)
        {
            Log.Write("Sensor initialization failed: " + ex);
            await _dispatcher.InvokeAsync(() => Banner = "Sensor initialization failed · see log.txt in the settings folder");
            return;
        }

        if (_demo && !_backlogLoaded)
        {
            // Pre-fill history so the demo looks like it has been running for a while.
            _backlogLoaded = true;
            int count = _cards.Values.First().History.Capacity - 1;
            var backlog = Enumerable.Range(0, count).Select(_ => _source.Read(_preferences)).ToList();
            await _dispatcher.InvokeAsync(() => backlog.ForEach(Apply));
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.PollIntervalMs));
        try
        {
            do
            {
                try
                {
                    var snapshot = _source.Read(_preferences);
                    var usage = SampleUsageIfDue();
                    await _dispatcher.InvokeAsync(() =>
                    {
                        Apply(snapshot);
                        if (usage != null)
                            ApplyUsage(usage);
                    }, DispatcherPriority.Normal, ct);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad read shouldn't stop the dashboard; log at most once a minute.
                    if (DateTime.Now - _lastErrorLog > TimeSpan.FromMinutes(1))
                    {
                        _lastErrorLog = DateTime.Now;
                        Log.Write("Sensor read failed: " + ex);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
        }
    }

    void Apply(HardwareSnapshot snapshot)
    {
        foreach (var (kind, card) in _cards)
            card.Apply(snapshot[kind]);
        UpdateNight();
    }

    /// <summary>Per-app usage every couple of seconds, whatever the sensor interval (it's smoother and cheaper).</summary>
    UsageSnapshot? SampleUsageIfDue()
    {
        if (DateTime.UtcNow - _lastUsageSample < UsageInterval)
            return null;
        _lastUsageSample = DateTime.UtcNow;
        try
        {
            return _usage.Sample();
        }
        catch (Exception ex)
        {
            if (DateTime.Now - _lastErrorLog > TimeSpan.FromMinutes(1))
            {
                _lastErrorLog = DateTime.Now;
                Log.Write("Process usage sample failed: " + ex);
            }
            return null;
        }
    }

    void ApplyUsage(UsageSnapshot usage)
    {
        foreach (var (kind, card) in _cards)
            card.ApplyUsage(usage.For(kind));
    }

    void OnThemeChanged()
    {
        foreach (var card in _cards.Values)
            card.ApplyTheme();
    }

    void UpdateNight()
    {
        var night = _settings.Night;
        bool anyHot = Components.Any(c => c.Level == TempLevel.Hot);
        bool dim = night.IsNight(DateTime.Now) && DateTime.Now >= _wakeUntil && !(night.WakeWhenHot && anyHot);
        DimOpacity = !dim ? 0 : night.Black ? 1 : 1 - night.Brightness;
    }

    void OnClockTick()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        bool twentyFourHour = culture.DateTimeFormat.ShortTimePattern.Contains('H');

        ClockText = now.ToString(twentyFourHour ? "HH:mm" : "h:mm", culture);
        ClockSuffix = twentyFourHour ? "" : now.ToString("tt", culture);
        DateText = now.ToString("dddd  ·  MMM d", culture);

        var session = now - _sessionStart;
        SessionText = $"Session {(int)session.TotalHours}:{session.Minutes:00}:{session.Seconds:00}  ·  since {_sessionStart.ToString("t", culture)}";

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText = uptime.TotalDays >= 1
            ? $"Uptime {(int)uptime.TotalDays}d {uptime.Hours:00}:{uptime.Minutes:00}"
            : $"Uptime {uptime.Hours}:{uptime.Minutes:00}";

        if (Toast != null && now >= _toastUntil)
            Toast = null;
        UpdateNight();
    }

    public void Dispose()
    {
        ThemeManager.Changed -= OnThemeChanged;
        _clock.Stop();
        _pollCts.Cancel();
        _source.Dispose();
        _usage.Dispose();
    }
}
