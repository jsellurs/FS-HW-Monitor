using System.Diagnostics;
using FSHWMonitor.Core;
using Microsoft.Win32;

namespace FSHWMonitor.Platform;

/// <summary>
/// CPU and DIMM sensors are read through PawnIO, a signed open-source kernel driver that replaced
/// the old WinRing0 driver (which Windows Defender now blocks). It is installed once per machine.
/// </summary>
public static class DriverHelper
{
    public const string PawnIoUrl = "https://pawnio.eu/";

    public static bool IsPawnIoInstalled
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                return key != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Installs PawnIO with winget in a visible console, or opens its website if winget is missing.</summary>
    public static void Install()
    {
        try
        {
            Process.Start(new ProcessStartInfo("winget",
                "install --id namazso.PawnIO --exact --source winget --accept-package-agreements --accept-source-agreements")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Write("winget not available for PawnIO install: " + ex.Message);
            Shell.Open(PawnIoUrl);
        }
    }
}
