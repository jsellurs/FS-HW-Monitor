using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FSHWMonitor.Platform;

/// <summary>Finds the in-case display and makes the window cover it exactly, in physical pixels.</summary>
public static class DisplayManager
{
    /// <summary>
    /// Sizes of common in-case / accessory screens, picked automatically (either orientation):
    /// 1280×800 panels, 8.8" 1920×480 strips, Xeneon Edge 2560×720, Hyte Y70 2560×682,
    /// and 7" 1024×600 / 5" 800×480 HDMI screens.
    /// </summary>
    static readonly (int Width, int Height)[] KnownPanels =
        new (int, int)[] { (1280, 800), (1920, 480), (1920, 440), (2560, 720), (2560, 682), (1280, 480), (1280, 400), (1024, 600), (800, 480) }
            .SelectMany(p => new[] { p, (p.Item2, p.Item1) })
            .ToArray();

    public sealed record DisplayInfo(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary)
    {
        public string ShortName => DeviceName.Replace(@"\\.\", "");
        public string Label => $"{ShortName}  ·  {Width}×{Height}{(IsPrimary ? "  ·  primary" : "")}";
    }

    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref Rect _, IntPtr _) =>
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var r = info.rcMonitor;
                list.Add(new DisplayInfo(info.szDevice, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                    (info.dwFlags & MonitorInfoFPrimary) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// The display to cover: the one named in settings/arguments if present, otherwise the first
    /// non-primary display with a known in-case panel size. Null means no dedicated display was found.
    /// </summary>
    public static DisplayInfo? FindTarget(string? preferredDevice)
    {
        var displays = GetDisplays().OrderBy(d => d.IsPrimary).ToList();
        if (!string.IsNullOrWhiteSpace(preferredDevice))
        {
            var preferred = displays.FirstOrDefault(d =>
                d.DeviceName.Equals(preferredDevice, StringComparison.OrdinalIgnoreCase) ||
                d.ShortName.Equals(preferredDevice, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
                return preferred;
        }

        return displays.FirstOrDefault(d => !d.IsPrimary && KnownPanels.Contains((d.Width, d.Height)));
    }

    public static void Cover(Window window, DisplayInfo display, bool topmost)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNoTopmost,
            display.X, display.Y, display.Width, display.Height, SwpNoActivate);
    }

    // Win32

    const uint MonitorInfoFPrimary = 1;
    const uint SwpNoActivate = 0x0010;
    static readonly IntPtr HwndTopmost = new(-1);
    static readonly IntPtr HwndNoTopmost = new(-2);

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect bounds, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
