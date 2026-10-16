using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FSHWMonitor.Core;
using FSHWMonitor.Platform;
using FSHWMonitor.Sensors;
using FSHWMonitor.Theme;
using FSHWMonitor.ViewModels;
using FSHWMonitor.Views;
using Microsoft.Win32;

namespace FSHWMonitor;

public partial class MainWindow : Window, IDashboardHost
{
    readonly AppSettings _settings;
    readonly StartupOptions _options;
    readonly MainViewModel _viewModel;
    DisplayManager.DisplayInfo? _display;
    LayoutPlan _plan = LayoutPlanner.Plan(LayoutMode.Landscape, 1.6, 4);
    bool _placementPending;
    bool _startupTaskEnabled;
    bool _exiting;
    SettingsWindow? _settingsWindow;
    TrayIcon? _tray;

    public MainWindow(AppSettings settings, StartupOptions options)
    {
        InitializeComponent();
        _settings = settings;
        _options = options;

        ISensorSource source = options.Demo ? new DemoSensorSource() : new LhmSensorSource(settings.ReadUsbCoolers);
        IProcessUsageSource usage = options.Demo ? new DemoProcessUsage() : new ProcessUsageMonitor();
        _viewModel = new MainViewModel(settings, source, usage, options.Demo);
        _viewModel.CardsChanged += SchedulePlacement;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.DimOpacity))
                NightVeil.BeginAnimation(OpacityProperty, new DoubleAnimation(_viewModel.DimOpacity, TimeSpan.FromSeconds(1.5)));
        };
        DataContext = _viewModel;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += (_, _) => BuildMenu();
        PreviewMouseDown += (_, _) =>
        {
            if (_viewModel.DimOpacity > 0)
                _viewModel.WakeBriefly();
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    bool IsSnapshot => _options.SnapshotPath is not null;
    bool Windowed => _options.Windowed || _settings.DisplayDevice == AppSettings.WindowedDisplay;
    bool CloseToTray => !IsSnapshot && (_options.CloseToTray || _settings.CloseToTray);

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        PlaceWindow();
        if (!IsSnapshot)
            SingleInstance.Listen(this, BringToFront, ExitApp);
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTrayIcon();
        _viewModel.Start();

        if (Elevation.IsAdministrator && !_options.Demo)
            Task.Run(() => _startupTaskEnabled = StartupTask.IsEnabled());

        if (_options.SnapshotPath is { } snapshotPath)
            ScheduleSnapshot(snapshotPath);
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4, the title-bar X or the taskbar's "Close window" hide to the tray when that's on;
        // Exit (menu, tray, or a restart from a second copy) always quits.
        if (!_exiting && CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _settingsWindow?.Close();
        _tray?.Dispose();
        _viewModel.Dispose();
    }

    void ExitApp()
    {
        _exiting = true;
        Close();
    }

    // Tray

    void UpdateTrayIcon()
    {
        if (CloseToTray && _tray is null)
        {
            _tray = new TrayIcon(RestoreWindow, OpenSettings, ExitApp);
        }
        else if (!CloseToTray && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
            RestoreWindow(); // never leave the window hidden with no icon to bring it back
        }
    }

    void HideToTray()
    {
        Hide();
        _tray?.ShowHiddenTip();
    }

    void RestoreWindow()
    {
        if (!IsVisible)
        {
            Show();
            PlaceWindow();
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    void ScheduleSnapshot(string path)
    {
        if (_options.SnapshotSettings)
        {
            OpenSettings();
            _settingsWindow?.SelectTab(_options.SnapshotSettingsTab);
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_options.SnapshotDelaySeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (_options.SnapshotSettings && _settingsWindow?.Content is FrameworkElement page)
                    Snapshot.SavePng(page, path, (int)page.ActualWidth, (int)page.ActualHeight);
                else if (_options.SnapshotUsage is { } usageKind)
                    Snapshot.SaveDetached(BuildUsagePopupPreview(_viewModel.Card(usageKind)), path);
                else
                    Snapshot.SavePng(Root, path, (int)_plan.Canvas.Width, (int)_plan.Canvas.Height);

                using var self = System.Diagnostics.Process.GetCurrentProcess();
                Log.Write($"Snapshot saved. Managed heap {GC.GetTotalMemory(false) / 1048576} MB, " +
                          $"private {self.PrivateMemorySize64 / 1048576} MB, CPU {self.TotalProcessorTime.TotalSeconds:0.0} s");
            }
            catch (Exception ex)
            {
                Log.Write("Snapshot failed: " + ex);
            }
            Close();
        };
        timer.Start();
    }

    /// <summary>The "Most used" popup as it would appear, laid out on its own for a screenshot.</summary>
    FrameworkElement BuildUsagePopupPreview(ComponentViewModel card) => new Border
    {
        // Same box as the UsageToolTip template, on the dashboard backdrop.
        Background = (Brush)FindResource("WindowBackgroundBrush"),
        Padding = new Thickness(24),
        Child = new Border
        {
            Width = 310,
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = (Brush)FindResource("CardBackgroundBrush"),
            BorderBrush = (Brush)FindResource("DividerBrush"),
            Child = new ContentControl
            {
                Content = card,
                ContentTemplate = (DataTemplate)FindResource("UsagePanelTemplate"),
                FontFamily = (FontFamily)FindResource("TitleFont"),
            },
        },
    };

    /// <summary>A second launch chose "Show it": come back even from behind full-screen windows.</summary>
    void BringToFront()
    {
        RestoreWindow();
        if (_display != null)
            DisplayManager.Cover(this, _display, topmost: true);
        Topmost = true;
        Topmost = _settings.AlwaysOnTop;
        if (_display != null)
            DisplayManager.Cover(this, _display, _settings.AlwaysOnTop);
        _viewModel.WakeBriefly();
        _viewModel.ShowToast("FS HW Monitor is already running");
    }

    // Placement and layout

    /// <summary>Coalesces several card changes into one layout pass.</summary>
    void SchedulePlacement()
    {
        if (_placementPending) return;
        _placementPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _placementPending = false;
            PlaceWindow();
        });
    }

    void PlaceWindow()
    {
        var target = Windowed ? null : DisplayManager.FindTarget(_options.Display ?? _settings.DisplayDevice);
        double? aspect = target is null ? null : (double)target.Width / target.Height;
        ApplyLayout(LayoutPlanner.Plan(_options.Layout ?? _settings.GetLayout(), aspect, _viewModel.Components.Count));

        if (target is null)
        {
            EnterWindowedMode();
            return;
        }

        _display = target;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = _settings.AlwaysOnTop;
        DisplayManager.Cover(this, target, _settings.AlwaysOnTop);

        // Moving onto a monitor with a different DPI makes WPF rescale the window; snap back to the exact bounds.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_display == target)
                DisplayManager.Cover(this, target, _settings.AlwaysOnTop);
        });
    }

    void ApplyLayout(LayoutPlan plan)
    {
        _plan = plan;
        Root.Width = plan.Canvas.Width;
        Root.Height = plan.Canvas.Height;

        CardsHost.ItemTemplate = (DataTemplate)FindResource(plan.CardStyle switch
        {
            CardStyle.Column => "ColumnCardTemplate",
            CardStyle.Row => "RowCardTemplate",
            _ => "CompactCardTemplate",
        });

        var grid = new FrameworkElementFactory(typeof(UniformGrid));
        grid.SetValue(UniformGrid.RowsProperty, plan.Rows);
        grid.SetValue(UniformGrid.ColumnsProperty, plan.Columns);
        CardsHost.ItemsPanel = new ItemsPanelTemplate(grid);

        var container = new Style(typeof(ContentPresenter));
        container.Setters.Add(new Setter(MarginProperty, plan.CardMargin));
        CardsHost.ItemContainerStyle = container;
        CardsHost.Margin = plan.HostMargin;

        // Narrow canvases: the footer banner gets its own line.
        bool compact = plan.Compact, narrow = plan.Narrow;
        FooterBanner.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0);

        // Tall strips stack the footer: session line, then the hint line, both centered.
        Grid.SetRow(FooterLeft, compact ? 1 : 0);
        Grid.SetRow(FooterRight, narrow ? 2 : compact ? 1 : 0);
        FooterLeft.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        FooterRight.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        FooterRight.Margin = narrow ? new Thickness(0, 6, 0, 0) : new Thickness(0);
    }

    void EnterWindowedMode()
    {
        _display = null;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        Topmost = _settings.AlwaysOnTop;

        var area = SystemParameters.WorkArea;
        var size = _plan.Canvas;
        double scale = Math.Min(1, Math.Min(area.Width * 0.95 / size.Width, area.Height * 0.92 / size.Height));
        Width = size.Width * scale;
        Height = size.Height * scale;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!Windowed)
                PlaceWindow();
        });

    // Settings window

    void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    // IDashboardHost

    public AppSettings Settings => _settings;
    public ISensorSource Sensors => _viewModel.Sensors;
    public bool IsDemo => _options.Demo;
    public bool StartupTaskEnabled => _startupTaskEnabled;

    public void ApplyTheme(AppTheme theme)
    {
        _settings.Theme = theme.Name;
        _settings.Save();
        ThemeManager.Apply(theme);
    }

    public void ApplyPlacement()
    {
        _settings.Save();
        PlaceWindow();
    }

    public void ApplyUnits()
    {
        _settings.Save();
        _viewModel.SetFahrenheit(_settings.UseFahrenheit);
    }

    public void ApplyCloseToTray()
    {
        _settings.Save();
        UpdateTrayIcon();
    }

    public void ApplyAlwaysOnTop()
    {
        _settings.Save();
        Topmost = _settings.AlwaysOnTop;
        if (_display != null)
            DisplayManager.Cover(this, _display, _settings.AlwaysOnTop);
    }

    public void ApplyCards()
    {
        _settings.Save();
        _viewModel.ApplyCardSettings();
    }

    public void ApplyPolling()
    {
        _settings.Save();
        _viewModel.ApplyPollingSettings();
    }

    public void ApplyNight()
    {
        _settings.Save();
        _viewModel.ApplyNightSettings();
    }

    public bool SetStartupTask(bool enabled)
    {
        if (enabled == _startupTaskEnabled)
            return true;

        bool ok = enabled ? StartupTask.Enable(Environment.ProcessPath!) : StartupTask.Disable();
        if (ok)
            _startupTaskEnabled = enabled;
        else
            MessageBox.Show("Could not update the startup task. See log.txt in the settings folder.",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        return ok;
    }

    public async void SaveSensorReport()
    {
        try
        {
            string report = await _viewModel.BuildReportAsync();
            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"FS HW Monitor sensors {DateTime.Now:yyyy-MM-dd HHmmss}.txt");
            await File.WriteAllTextAsync(path, report);
            _viewModel.ShowToast("Sensor report saved to your Desktop");
        }
        catch (Exception ex)
        {
            Log.Write("Saving sensor report failed: " + ex);
            MessageBox.Show("Could not save the sensor report: " + ex.Message, AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Right-click menu

    void BuildMenu()
    {
        var menu = ContextMenu;
        menu.Items.Clear();

        menu.Items.Add(Item("Settings…", OpenSettings));
        menu.Items.Add(new Separator());

        ThemeStore.Reload();
        var themes = Submenu("Theme");
        foreach (var theme in ThemeStore.All)
            themes.Items.Add(Item(ThemeHeader(theme), () => ApplyTheme(theme), isChecked: theme.Name == ThemeManager.Current.Name));
        themes.Items.Add(new Separator());
        themes.Items.Add(Item("Save current theme as a file…", SaveThemeTemplate));
        themes.Items.Add(Item("Open themes folder", () => Shell.OpenFolder(ThemeStore.Folder)));
        menu.Items.Add(themes);

        var layout = Submenu("Layout");
        foreach (var (mode, label) in LayoutLabels)
            layout.Items.Add(Item(label, () => SetLayout(mode), isChecked: _settings.GetLayout() == mode));
        menu.Items.Add(layout);

        var cards = Submenu("Cards");
        foreach (var card in _settings.Cards)
            cards.Items.Add(Item(card.Kind.Title(), () => ToggleCard(card), isChecked: card.Visible));
        menu.Items.Add(cards);

        var units = Submenu("Units");
        units.Items.Add(Item("Celsius  °C", () => SetFahrenheit(false), isChecked: !_settings.UseFahrenheit));
        units.Items.Add(Item("Fahrenheit  °F", () => SetFahrenheit(true), isChecked: _settings.UseFahrenheit));
        menu.Items.Add(units);

        var display = Submenu("Display");
        display.Items.Add(Item("Automatic", () => SetDisplay(null), isChecked: _settings.DisplayDevice is null && !Windowed));
        foreach (var d in DisplayManager.GetDisplays())
            display.Items.Add(Item(d.Label, () => SetDisplay(d.DeviceName), isChecked: _settings.DisplayDevice == d.DeviceName && !Windowed));
        display.Items.Add(new Separator());
        display.Items.Add(Item("Windowed", () => SetDisplay(AppSettings.WindowedDisplay), isChecked: Windowed));
        menu.Items.Add(display);

        menu.Items.Add(Item("Always on top", () => { _settings.AlwaysOnTop = !_settings.AlwaysOnTop; ApplyAlwaysOnTop(); }, isChecked: _settings.AlwaysOnTop));
        menu.Items.Add(Item("Close to tray", () => { _settings.CloseToTray = !_settings.CloseToTray; ApplyCloseToTray(); }, isChecked: _settings.CloseToTray));
        menu.Items.Add(Item("Night mode", () => { _settings.Night.Enabled = !_settings.Night.Enabled; ApplyNight(); }, isChecked: _settings.Night.Enabled));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Reset high / avg / low", _viewModel.ResetAllStats));
        if (!DriverHelper.IsPawnIoInstalled && !_options.Demo)
            menu.Items.Add(Item("Install sensor driver (PawnIO)…", InstallDriver));
        menu.Items.Add(Item("Save sensor report to Desktop", SaveSensorReport));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("About FS HW Monitor", ShowAbout));
        if (CloseToTray)
            menu.Items.Add(Item("Hide to tray", HideToTray));
        menu.Items.Add(Item("Exit", ExitApp));
    }

    public static readonly IReadOnlyList<(LayoutMode Mode, string Label)> LayoutLabels =
    [
        (LayoutMode.Auto, "Automatic (fits the display)"),
        (LayoutMode.Landscape, "Landscape  ·  e.g. 1280×800"),
        (LayoutMode.Portrait, "Portrait  ·  e.g. 800×1280"),
        (LayoutMode.Bar, "Wide strip  ·  e.g. 1920×480"),
        (LayoutMode.TallBar, "Tall strip  ·  e.g. 480×1920"),
        (LayoutMode.Grid, "Grid  ·  square screens"),
    ];

    static MenuItem Submenu(string text) => new() { Header = text };

    static MenuItem Item(object header, Action action, bool isChecked = false)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Theme name with a dot for each of its card colors.</summary>
    public static StackPanel ThemeHeader(AppTheme theme)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var color in new[] { theme.Cpu, theme.Gpu, theme.Ram, theme.Ssd })
        {
            panel.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = Palette.Brush(color),
                Stroke = Palette.Brush(Colors.White, 0x30),
                StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        panel.Children.Add(new TextBlock { Text = theme.Name, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    void SetLayout(LayoutMode mode)
    {
        _settings.Layout = mode.ToString();
        ApplyPlacement();
    }

    void SetDisplay(string? device)
    {
        _settings.DisplayDevice = device;
        ApplyPlacement();
    }

    void SetFahrenheit(bool fahrenheit)
    {
        _settings.UseFahrenheit = fahrenheit;
        ApplyUnits();
    }

    void ToggleCard(CardSettings card)
    {
        if (card.Visible && _settings.Cards.Count(c => c.Visible) == 1)
            return; // keep at least one card
        card.Visible = !card.Visible;
        ApplyCards();
    }

    void SaveThemeTemplate()
    {
        string path = ThemeStore.SaveTemplate(ThemeManager.Current);
        _viewModel.ShowToast($"Saved {System.IO.Path.GetFileName(path)} to the themes folder", 6);
        Shell.OpenFolder(ThemeStore.Folder);
    }

    void InstallDriver()
    {
        var answer = MessageBox.Show(this,
            "CPU, RAM and fan readings come through PawnIO, a signed open-source driver.\n\n" +
            "Install it now with winget? Restart FS HW Monitor when the install finishes.",
            AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
            DriverHelper.Install();
    }

    void ShowAbout() => MessageBox.Show(this,
        $"{AppInfo.Name} {AppInfo.Version}\n\n" +
        "Live CPU, GPU, RAM, SSD and cooling temperatures for in-case displays.\n\n" +
        "Sensor access: LibreHardwareMonitorLib (MPL-2.0) and the PawnIO driver.\n" +
        "Tip: double-click a card to reset its high / avg / low.",
        AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
}
