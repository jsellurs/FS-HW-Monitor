using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using FSHWMonitor.Core;
using FSHWMonitor.Platform;
using FSHWMonitor.Sensors;
using FSHWMonitor.Theme;
using FSHWMonitor.Views;

namespace FSHWMonitor.ViewModels;

/// <summary>A labelled choice for a ComboBox.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Backs the Settings window. Every change is applied to the running dashboard right away and saved,
/// so there is no OK / Apply step.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    readonly IDashboardHost _host;

    public SettingsViewModel(IDashboardHost host)
    {
        _host = host;

        Layouts = MainWindow.LayoutLabels.Select(l => new Option<LayoutMode>(l.Mode, l.Label)).ToList();
        Displays =
        [
            new(null, "Automatic (find the in-case screen)"),
            .. DisplayManager.GetDisplays().Select(d => new Option<string?>(d.DeviceName, d.Label)),
            new(AppSettings.WindowedDisplay, "Windowed (a normal window)"),
        ];
        Intervals =
        [
            new(500, "Twice a second"),
            new(1000, "Every second"),
            new(2000, "Every 2 seconds"),
            new(5000, "Every 5 seconds"),
        ];
        HistoryLengths = [new(1, "1 minute"), new(5, "5 minutes"), new(15, "15 minutes"), new(30, "30 minutes"), new(60, "1 hour")];

        Cards = new(S.Cards.Select(c => new CardRowViewModel(this, c)));
        LoadThemes();
        LoadFans();

        OpenThemesFolderCommand = new RelayCommand(() => Shell.OpenFolder(ThemeStore.Folder));
        SaveThemeTemplateCommand = new RelayCommand(SaveThemeTemplate);
        ReloadThemesCommand = new RelayCommand(LoadThemes);
        RefreshFansCommand = new RelayCommand(LoadFans);
        SaveReportCommand = new RelayCommand(_host.SaveSensorReport);
        OpenSettingsFolderCommand = new RelayCommand(() => Shell.OpenFolder(AppSettings.Folder));
        InstallDriverCommand = new RelayCommand(DriverHelper.Install);
    }

    AppSettings S => _host.Settings;
    internal bool Fahrenheit => S.UseFahrenheit;

    // General

    public ObservableCollection<AppTheme> Themes { get; } = [];

    public AppTheme? SelectedTheme
    {
        get => Themes.FirstOrDefault(t => t.Name == ThemeManager.Current.Name);
        set
        {
            if (value is null || value.Name == ThemeManager.Current.Name) return;
            _host.ApplyTheme(value);
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<Option<LayoutMode>> Layouts { get; }

    public Option<LayoutMode> SelectedLayout
    {
        get => Layouts.First(o => o.Value == S.GetLayout());
        set
        {
            S.Layout = value.Value.ToString();
            _host.ApplyPlacement();
        }
    }

    public IReadOnlyList<Option<string?>> Displays { get; }

    public Option<string?> SelectedDisplay
    {
        get => Displays.FirstOrDefault(o => o.Value == S.DisplayDevice) ?? Displays[0];
        set
        {
            S.DisplayDevice = value.Value;
            _host.ApplyPlacement();
        }
    }

    public int UnitsIndex
    {
        get => S.UseFahrenheit ? 1 : 0;
        set
        {
            S.UseFahrenheit = value == 1;
            _host.ApplyUnits();
            foreach (var card in Cards)
                card.RefreshTemperatures();
            OnPropertyChanged(nameof(UnitLabel));
        }
    }

    public string UnitLabel => S.UseFahrenheit ? "°F" : "°C";

    public IReadOnlyList<Option<int>> Intervals { get; }

    public Option<int> SelectedInterval
    {
        get => Intervals.FirstOrDefault(o => o.Value == S.PollIntervalMs) ?? Intervals[1];
        set
        {
            S.PollIntervalMs = value.Value;
            _host.ApplyPolling();
        }
    }

    public IReadOnlyList<Option<int>> HistoryLengths { get; }

    public Option<int> SelectedHistory
    {
        get => HistoryLengths.FirstOrDefault(o => o.Value == S.HistoryMinutes) ?? HistoryLengths[1];
        set
        {
            S.HistoryMinutes = value.Value;
            _host.ApplyPolling();
        }
    }

    public bool AlwaysOnTop
    {
        get => S.AlwaysOnTop;
        set
        {
            S.AlwaysOnTop = value;
            _host.ApplyAlwaysOnTop();
        }
    }

    public bool CloseToTray
    {
        get => S.CloseToTray;
        set
        {
            S.CloseToTray = value;
            _host.ApplyCloseToTray();
        }
    }

    static bool IsInstalledCopy => StartupTask.IsProtectedLocation(Environment.ProcessPath!);

    /// <summary>Turning it on needs the installed copy; turning it off always works.</summary>
    public bool CanStartWithWindows => Elevation.IsAdministrator && !_host.IsDemo && (IsInstalledCopy || StartWithWindows);

    public bool StartWithWindows
    {
        get => _host.StartupTaskEnabled;
        set
        {
            _host.SetStartupTask(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartWithWindows));
        }
    }

    public string StartWithWindowsHint => IsInstalledCopy
        ? "Starts the installed copy when you sign in."
        : "Needs the installed version. A copy outside Program Files could be swapped by another program to get administrator rights when you sign in.";

    // Cards

    public ObservableCollection<CardRowViewModel> Cards { get; }

    public CardRowViewModel CoolingCard => Cards.First(c => c.Kind == ComponentKind.Cooling);

    internal void CardsChanged() => _host.ApplyCards();

    internal IReadOnlyList<SensorChoice> SensorChoices(ComponentKind kind) => _host.Sensors.GetTemperatureChoices(kind);

    internal bool IsLastVisible(CardRowViewModel row) => row.Visible && Cards.Count(c => c.Visible) == 1;

    internal void Move(CardRowViewModel row, int delta)
    {
        int from = Cards.IndexOf(row);
        int to = Math.Clamp(from + delta, 0, Cards.Count - 1);
        if (from == to) return;

        Cards.Move(from, to);
        var card = S.Cards[from];
        S.Cards.RemoveAt(from);
        S.Cards.Insert(to, card);
        CardsChanged();
    }

    // Cooling

    public ObservableCollection<FanRowViewModel> Fans { get; } = [];

    public bool NoFans => Fans.Count == 0;

    bool _usbRestartNeeded;
    public bool UsbRestartNeeded { get => _usbRestartNeeded; private set => Set(ref _usbRestartNeeded, value); }

    public bool ReadUsbCoolers
    {
        get => S.ReadUsbCoolers;
        set
        {
            S.ReadUsbCoolers = value;
            S.Save();
            UsbRestartNeeded = true;
        }
    }

    void LoadFans()
    {
        Fans.Clear();
        foreach (var fan in _host.Sensors.GetFans())
            Fans.Add(new FanRowViewModel(this, fan.Id, fan.Name, fan.Rpm));
        OnPropertyChanged(nameof(NoFans));
        CoolingCard.ReloadSensorOptions();
    }

    internal FanSettings FanSettingsFor(string id)
    {
        if (!S.Fans.TryGetValue(id, out var fan))
            S.Fans[id] = fan = new FanSettings();
        return fan;
    }

    // Night mode

    public bool NightEnabled
    {
        get => S.Night.Enabled;
        set { S.Night.Enabled = value; _host.ApplyNight(); }
    }

    public string NightStart
    {
        get => S.Night.Start;
        set { S.Night.Start = ValidTime(value); _host.ApplyNight(); }
    }

    public string NightEnd
    {
        get => S.Night.End;
        set { S.Night.End = ValidTime(value); _host.ApplyNight(); }
    }

    public int NightModeIndex
    {
        get => S.Night.Black ? 1 : 0;
        set { S.Night.Black = value == 1; _host.ApplyNight(); OnPropertyChanged(nameof(CanSetBrightness)); }
    }

    public bool CanSetBrightness => !S.Night.Black;

    public double NightBrightness
    {
        get => S.Night.Brightness * 100;
        set
        {
            S.Night.Brightness = Math.Clamp(value / 100, 0.05, 0.9);
            _host.ApplyNight();
            OnPropertyChanged(nameof(NightBrightnessText));
        }
    }

    public string NightBrightnessText => $"{S.Night.Brightness:P0}";

    public bool WakeWhenHot
    {
        get => S.Night.WakeWhenHot;
        set { S.Night.WakeWhenHot = value; _host.ApplyNight(); }
    }

    static string ValidTime(string text) => NightSettings.TryParseTime(text, out var time)
        ? time.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
        : throw new ArgumentException("Use 24-hour time, like 23:00");

    // Themes and about

    public ICommand OpenThemesFolderCommand { get; }
    public ICommand SaveThemeTemplateCommand { get; }
    public ICommand ReloadThemesCommand { get; }
    public ICommand RefreshFansCommand { get; }
    public ICommand SaveReportCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }
    public ICommand InstallDriverCommand { get; }

    public string ThemesFolder => ThemeStore.Folder;
    public string Version => $"{AppInfo.Name} {AppInfo.Version}";
    public bool DriverMissing => !DriverHelper.IsPawnIoInstalled && !_host.IsDemo;

    string _themeMessage = "";
    public string ThemeMessage { get => _themeMessage; private set => Set(ref _themeMessage, value); }

    void LoadThemes()
    {
        ThemeStore.Reload();
        Themes.Clear();
        foreach (var theme in ThemeStore.All)
            Themes.Add(theme);
        int custom = Themes.Count(t => t.IsCustom);
        ThemeMessage = custom == 0 ? "No custom themes yet." : $"{custom} custom theme{(custom == 1 ? "" : "s")} loaded.";
        OnPropertyChanged(nameof(SelectedTheme));
    }

    void SaveThemeTemplate()
    {
        string path = ThemeStore.SaveTemplate(ThemeManager.Current);
        LoadThemes();
        ThemeMessage = $"Saved {Path.GetFileName(path)}. Edit it, then click Reload themes.";
        Shell.OpenFolder(ThemeStore.Folder);
    }

    // Shared helpers for rows

    internal string FormatTemp(double celsius) =>
        Math.Round(Fahrenheit ? Units.ToFahrenheit(celsius) : celsius).ToString("0", CultureInfo.CurrentCulture);

    internal double ParseTemp(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value))
            throw new ArgumentException("Enter a number");
        return Fahrenheit ? Units.ToCelsius(value) : value;
    }
}

/// <summary>One card in the Cards tab.</summary>
public sealed class CardRowViewModel : ObservableObject
{
    readonly SettingsViewModel _owner;
    readonly CardSettings _card;

    public CardRowViewModel(SettingsViewModel owner, CardSettings card)
    {
        _owner = owner;
        _card = card;
        MoveUpCommand = new RelayCommand(() => _owner.Move(this, -1));
        MoveDownCommand = new RelayCommand(() => _owner.Move(this, 1));
        ReloadSensorOptions();
    }

    public ComponentKind Kind => _card.Kind;
    public string Title => _card.Kind.Title();
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public bool Visible
    {
        get => _card.Visible;
        set
        {
            if (!value && _owner.IsLastVisible(this))
            {
                OnPropertyChanged(); // keep at least one card on screen
                return;
            }
            _card.Visible = value;
            _owner.CardsChanged();
        }
    }

    IReadOnlyList<Option<string?>> _sensorOptions = [];
    public IReadOnlyList<Option<string?>> SensorOptions { get => _sensorOptions; private set => Set(ref _sensorOptions, value); }

    public Option<string?> SelectedSensor
    {
        get => SensorOptions.FirstOrDefault(o => o.Value == _card.Sensor) ?? SensorOptions[0];
        set
        {
            if (value is null || value.Value == _card.Sensor) return;
            _card.Sensor = value.Value;
            _owner.CardsChanged();
        }
    }

    public void ReloadSensorOptions()
    {
        var options = new List<Option<string?>> { new(null, "Automatic") };
        options.AddRange(_owner.SensorChoices(_card.Kind).Select(c => new Option<string?>(c.Id, c.Label)));
        if (_card.Sensor != null && options.All(o => o.Value != _card.Sensor))
            options.Add(new Option<string?>(_card.Sensor, "Saved sensor (not found right now)"));
        SensorOptions = options;
        OnPropertyChanged(nameof(SelectedSensor));
    }

    public string Warn
    {
        get => _owner.FormatTemp(_card.Warn);
        set
        {
            double v = _owner.ParseTemp(value);
            if (v >= _card.Crit) throw new ArgumentException("Warm must be below Hot");
            _card.Warn = v;
            _owner.CardsChanged();
        }
    }

    public string Crit
    {
        get => _owner.FormatTemp(_card.Crit);
        set
        {
            double v = _owner.ParseTemp(value);
            if (v <= _card.Warn || v > _card.Max) throw new ArgumentException("Hot must be between Warm and the gauge max");
            _card.Crit = v;
            _owner.CardsChanged();
        }
    }

    public string Max
    {
        get => _owner.FormatTemp(_card.Max);
        set
        {
            double v = _owner.ParseTemp(value);
            if (v < _card.Crit || v <= _card.Min + 10) throw new ArgumentException("Max must be at least Hot");
            _card.Max = v;
            _owner.CardsChanged();
        }
    }

    public void RefreshTemperatures()
    {
        OnPropertyChanged(nameof(Warn));
        OnPropertyChanged(nameof(Crit));
        OnPropertyChanged(nameof(Max));
    }
}

/// <summary>One fan header in the Cooling tab.</summary>
public sealed class FanRowViewModel(SettingsViewModel owner, string id, string name, double? rpm) : ObservableObject
{
    public string Name { get; } = name;
    public string RpmText { get; } = rpm is double r ? $"{r.ToString("N0", CultureInfo.CurrentCulture)} RPM" : "n/a";

    public string Label
    {
        get => owner.FanSettingsFor(id).Label ?? "";
        set
        {
            owner.FanSettingsFor(id).Label = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            owner.CardsChanged();
        }
    }

    public bool Show
    {
        get => !owner.FanSettingsFor(id).Hidden;
        set
        {
            owner.FanSettingsFor(id).Hidden = !value;
            owner.CardsChanged();
        }
    }
}
