using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FSHWMonitor.Core;
using FSHWMonitor.Platform;
using FSHWMonitor.Sensors;
using FSHWMonitor.Theme;
using FSHWMonitor.Views;

namespace FSHWMonitor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Prompts may open before the dashboard; don't let closing one end the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ThemeManager.InstallBrandFonts();

        var options = StartupOptions.Parse(e.Args);
        bool isDashboard = options.SnapshotPath is null && options.DumpPath is null;
        // Demo mode is for trying things out, so it may run next to the real dashboard.
        bool singleInstance = isDashboard && !options.Demo;

        // Already running: offer to show it or restart it, before any UAC prompt.
        if (singleInstance && SingleInstance.IsRunning() && !HandleAlreadyRunning())
        {
            Shutdown();
            return;
        }

        // Most sensors need admin rights. Ask once via UAC; if declined, carry on with what we can read.
        if (!options.Demo && !options.NoElevate && !Elevation.IsAdministrator
            && Elevation.TryRelaunchElevated(options.NormalizedArgs))
        {
            Shutdown();
            return;
        }

        if (options.DumpPath is { } dumpPath)
        {
            WriteSensorDump(dumpPath, options.Demo);
            Shutdown();
            return;
        }

        if (singleInstance && !SingleInstance.TryClaim())
        {
            Shutdown();
            return;
        }

        if (options.SoftwareRendering)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var settings = AppSettings.Load();
        if (options.Cards is { Count: > 0 } cards)
        {
            // Show exactly these, in this order, for this run.
            settings.Cards = cards.Select(settings.Card)
                .Concat(settings.Cards.Where(c => !cards.Contains(c.Kind)))
                .ToList();
            settings.Cards.ForEach(c => c.Visible = cards.Contains(c.Kind));
        }
        if (isDashboard)
            ThemeStore.InstallStarterTemplate(settings);
        ThemeStore.Reload();
        ThemeManager.Apply(ThemeStore.Find(options.Theme ?? settings.Theme));

        var window = new MainWindow(settings, options);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Release();
        base.OnExit(e);
    }

    /// <summary>True to continue starting up (the old copy was closed for a restart).</summary>
    static bool HandleAlreadyRunning()
    {
        switch (AlreadyRunningWindow.Ask())
        {
            case AlreadyRunningWindow.Choice.Show:
                SingleInstance.AskRunningCopyToShow();
                return false;

            case AlreadyRunningWindow.Choice.Restart:
                if (SingleInstance.StopRunningCopy(TimeSpan.FromSeconds(10)))
                    return true;
                MessageBox.Show("The running copy of FS HW Monitor didn't close. Right-click it and choose Exit, then try again.",
                    AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;

            default:
                return false;
        }
    }

    static void WriteSensorDump(string path, bool demo)
    {
        try
        {
            using ISensorSource source = demo ? new DemoSensorSource() : new LhmSensorSource(AppSettings.Load().ReadUsbCoolers);
            using IProcessUsageSource usage = demo ? new DemoProcessUsage() : new ProcessUsageMonitor();
            source.Open();
            source.Read(SensorPreferences.Default);
            usage.Sample();
            Thread.Sleep(1000); // Some sensors (load, throughput) and per-app rates need two samples.

            var report = new System.Text.StringBuilder(source.BuildReport());
            var top = usage.Sample();
            foreach (var (title, list) in new[] { ("CPU", top.Cpu), ("GPU", top.Gpu), ("Memory", top.Ram), ("Disk", top.Disk) })
            {
                report.AppendLine().AppendLine($"Most used: {title}");
                foreach (var app in list)
                    report.AppendLine($"  {app.ValueText,12}  {app.Name}{(app.Count > 1 ? $" (×{app.Count})" : "")}  {app.Path}");
            }
            File.WriteAllText(path, report.ToString());
        }
        catch (Exception ex)
        {
            Log.Write("Sensor dump failed: " + ex);
        }
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A dashboard that lives on a case screen should keep running rather than vanish.
        Log.Write("Unhandled UI exception: " + e.Exception);
        e.Handled = true;
    }
}
