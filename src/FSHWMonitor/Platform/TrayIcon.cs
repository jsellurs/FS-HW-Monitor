using System.Runtime.InteropServices;
using System.Windows.Interop;
using FSHWMonitor.Core;

namespace FSHWMonitor.Platform;

/// <summary>
/// The notification-area icon used when "close to tray" is on: click it to bring the dashboard back;
/// right-click for Show, Settings and Exit. Talks to the shell directly (Shell_NotifyIcon) rather than
/// through WinForms, which would add ~20 MB to the single-file exe for one icon.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    const int MenuShow = 1, MenuSettings = 2, MenuExit = 3;

    readonly Action _show, _openSettings, _exit;
    readonly HwndSource _window;
    readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly IntPtr _icon;
    bool _tipShown;
    bool _disposed;

    public TrayIcon(Action show, Action openSettings, Action exit)
    {
        _show = show;
        _openSettings = openSettings;
        _exit = exit;

        // Hidden top-level window that receives the icon's clicks (and Explorer restarts).
        _window = new HwndSource(new HwndSourceParameters("FSHWMonitorTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _window.AddHook(WndProc);

        // The dashboard usually runs as administrator; let Explorer's messages through UIPI.
        ChangeWindowMessageFilterEx(_window.Handle, CallbackMessage, MsgfltAllow, IntPtr.Zero);
        ChangeWindowMessageFilterEx(_window.Handle, _taskbarCreated, MsgfltAllow, IntPtr.Zero);

        var small = IntPtr.Zero;
        ExtractIconEx(Environment.ProcessPath ?? "", 0, IntPtr.Zero, ref small, 1);
        _icon = small;

        Add();
    }

    /// <summary>One-time "still running" note the first time the window is hidden.</summary>
    public void ShowHiddenTip()
    {
        if (_tipShown || _disposed) return;
        _tipShown = true;
        var data = NewData(NifInfo);
        data.szInfoTitle = AppInfo.Name;
        data.szInfo = "Still running here. Click the icon to bring the dashboard back.";
        Shell_NotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = NewData(0);
        Shell_NotifyIcon(NimDelete, ref data);
        if (_icon != IntPtr.Zero)
            DestroyIcon(_icon);
        _window.Dispose();
    }

    void Add()
    {
        var data = NewData(NifMessage | NifIcon | NifTip);
        if (!Shell_NotifyIcon(NimAdd, ref data))
            Log.Write("Tray icon could not be added.");
    }

    NotifyIconData NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = AppInfo.Name,
        szInfo = "",
        szInfoTitle = "",
    };

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)lParam)
            {
                case WmLButtonUp:
                    _show();
                    break;
                case WmRButtonUp:
                    ShowMenu();
                    break;
            }
            handled = true;
        }
        else if (msg == (int)_taskbarCreated && !_disposed)
        {
            Add(); // Explorer restarted and forgot every icon
        }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MfString, MenuShow, $"Show {AppInfo.Name}");
            AppendMenu(menu, MfString, MenuSettings, "Settings…");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, MenuExit, "Exit");
            SetMenuDefaultItem(menu, MenuShow, 0);

            GetCursorPos(out var point);
            // Standard tray-menu dance so the menu closes when you click elsewhere.
            SetForegroundWindow(_window.Handle);
            int command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, point.X, point.Y, _window.Handle, IntPtr.Zero);
            PostMessage(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case MenuShow: _show(); break;
                case MenuSettings: _openSettings(); break;
                case MenuExit: _exit(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    // Win32

    const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10;
    const int WmLButtonUp = 0x0202, WmRButtonUp = 0x0205;
    const uint MfString = 0x0, MfSeparator = 0x800;
    const uint TpmReturnCmd = 0x100, TpmRightButton = 0x2, TpmBottomAlign = 0x20;
    const uint MsgfltAllow = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point
    {
        public int X, Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr large, ref IntPtr small, uint count);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);

    [DllImport("user32.dll")]
    static extern bool SetMenuDefaultItem(IntPtr menu, int item, uint byPosition);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
