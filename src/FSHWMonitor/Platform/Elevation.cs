using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using FSHWMonitor.Core;

namespace FSHWMonitor.Platform;

public static class Elevation
{
    public static bool IsAdministrator { get; } = CheckAdministrator();

    static bool CheckAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Starts a second copy of this exe through the UAC prompt. Returns false if the user
    /// declined, in which case the caller keeps running with limited sensors.
    /// </summary>
    public static bool TryRelaunchElevated(IEnumerable<string> args)
    {
        try
        {
            var arguments = args.Append("--no-elevate").Select(Quote);
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', arguments),
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // ERROR_CANCELLED: the user said no to UAC.
        }
        catch (Exception ex)
        {
            Log.Write("Elevated relaunch failed: " + ex.Message);
            return false;
        }
    }

    static string Quote(string arg) =>
        arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c == '"') ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";
}
