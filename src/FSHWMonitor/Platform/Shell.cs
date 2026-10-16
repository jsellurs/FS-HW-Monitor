using System.Diagnostics;
using System.IO;
using FSHWMonitor.Core;

namespace FSHWMonitor.Platform;

/// <summary>Opens links and folders with whatever Windows uses for them.</summary>
public static class Shell
{
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open {target}: {ex.Message}");
        }
    }

    /// <summary>Opens a folder in Explorer, creating it first if it doesn't exist yet.</summary>
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not create {folder}: {ex.Message}");
        }
        Open(folder);
    }
}
