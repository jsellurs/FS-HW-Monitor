using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FSHWMonitor.Platform;

/// <summary>Asks Windows 10/11 for a dark title bar so dialogs match the app.</summary>
public static class DarkTitleBar
{
    const int DwmwaUseImmersiveDarkMode = 20;

    public static void Enable(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            int on = 1;
            var hwnd = new WindowInteropHelper(window).Handle;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
