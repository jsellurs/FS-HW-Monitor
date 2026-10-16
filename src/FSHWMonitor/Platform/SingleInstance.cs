using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FSHWMonitor.Platform;

/// <summary>
/// One dashboard at a time. A second launch talks to the running copy through two registered
/// window messages: "show yourself" and "exit" (used for Restart, e.g. after updating the exe).
/// The running copy is usually elevated, so it has to let those messages through UIPI explicitly.
/// </summary>
public static class SingleInstance
{
    const string MutexName = @"Global\FSHWMonitor.SingleInstance";

    public static readonly uint ShowMessage = RegisterWindowMessage("FSHWMonitor.Show");
    public static readonly uint ExitMessage = RegisterWindowMessage("FSHWMonitor.Exit");

    static Mutex? _mutex;

    public static bool IsRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out var existing))
                return false;
            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // Exists, but owned by the elevated instance.
        }
    }

    /// <summary>Claims the single-instance slot. False if another copy got there first.</summary>
    public static bool TryClaim()
    {
        _mutex = new Mutex(true, MutexName, out bool created);
        if (created)
            return true;
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static void Release()
    {
        _mutex?.Dispose();
        _mutex = null;
    }

    public static void AskRunningCopyToShow()
    {
        AllowSetForegroundWindow(AsfwAny);
        PostMessage(HwndBroadcast, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Asks the running copy to exit and waits until it has.</summary>
    public static bool StopRunningCopy(TimeSpan timeout)
    {
        PostMessage(HwndBroadcast, ExitMessage, IntPtr.Zero, IntPtr.Zero);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning())
                return true;
            Thread.Sleep(150);
        }
        return !IsRunning();
    }

    /// <summary>Hooks a window up to receive the show/exit messages, even from a non-elevated sender.</summary>
    public static void Listen(Window window, Action onShow, Action onExit)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        ChangeWindowMessageFilterEx(hwnd, ShowMessage, MsgfltAllow, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, ExitMessage, MsgfltAllow, IntPtr.Zero);

        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if ((uint)msg == ShowMessage)
            {
                onShow();
                handled = true;
            }
            else if ((uint)msg == ExitMessage)
            {
                onExit();
                handled = true;
            }
            return IntPtr.Zero;
        });
    }

    static readonly IntPtr HwndBroadcast = new(0xFFFF);
    const int AsfwAny = -1;
    const uint MsgfltAllow = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr changeInfo);
}
