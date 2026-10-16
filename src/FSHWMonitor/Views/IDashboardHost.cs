using FSHWMonitor.Core;
using FSHWMonitor.Sensors;
using FSHWMonitor.Theme;

namespace FSHWMonitor.Views;

/// <summary>What the settings window can ask the running dashboard to do. Each Apply* also saves settings.</summary>
public interface IDashboardHost
{
    AppSettings Settings { get; }
    ISensorSource Sensors { get; }
    bool IsDemo { get; }

    void ApplyTheme(AppTheme theme);

    /// <summary>Display or layout changed.</summary>
    void ApplyPlacement();

    void ApplyUnits();
    void ApplyAlwaysOnTop();
    void ApplyCloseToTray();

    /// <summary>Card visibility, order, limits, gauge sensors or fan names changed.</summary>
    void ApplyCards();

    /// <summary>Update interval or history length changed.</summary>
    void ApplyPolling();

    void ApplyNight();

    bool StartupTaskEnabled { get; }

    /// <summary>Turns the logon task on or off; false if Windows refused.</summary>
    bool SetStartupTask(bool enabled);

    void SaveSensorReport();
}
