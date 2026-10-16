using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using FSHWMonitor.Core;

namespace FSHWMonitor.Platform;

/// <summary>
/// "Start with Windows" via a Task Scheduler logon task with highest privileges, so the
/// dashboard starts elevated at logon without a UAC prompt. Creating it requires admin.
/// </summary>
public static class StartupTask
{
    const string TaskName = "FS HW Monitor";

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Enable(string exePath)
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), "fshw-startup-task.xml");
        try
        {
            // schtasks only accepts UTF-16 task XML.
            File.WriteAllText(xmlPath, BuildTaskXml(exePath), Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    public static bool Disable() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

    static string BuildTaskXml(string exePath)
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string command = SecurityElement.Escape(exePath);
        string folder = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? "");

        // ExecutionTimeLimit PT0S: never stop the task (the default would kill it after 3 days).
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts FS HW Monitor at logon.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{command}"</Command>
                  <WorkingDirectory>{folder}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return -1;
            process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            if (process.ExitCode != 0 && !arguments.StartsWith("/Query"))
                Log.Write($"schtasks {arguments.Split(' ')[0]} failed: {error.Trim()}");
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Write("schtasks failed: " + ex.Message);
            return -1;
        }
    }
}
