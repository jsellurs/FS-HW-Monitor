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

    /// <summary>
    /// True when the exe is under Program Files, where only administrators can change it. The logon task
    /// runs its exe as administrator with no prompt, so pointing it at a copy in the user's own folders
    /// would let any program running as that user swap the exe and get admin rights at the next sign-in.
    /// </summary>
    public static bool IsProtectedLocation(string exePath)
    {
        string path = Path.GetFullPath(exePath);
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(folder => folder.Length > 0)
            .Any(folder => path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    public static bool Enable(string exePath)
    {
        if (!IsProtectedLocation(exePath))
        {
            Log.Write($"Start with Windows needs the installed copy; {exePath} is outside Program Files.");
            return false;
        }

        // schtasks only reads task XML (UTF-16) from a file, and any program the user runs can write to the
        // temp folder. So: a fresh file name, then hold the file open read-only (nobody may change or delete
        // it) and check it still says what we wrote, for as long as schtasks is reading it.
        byte[] xml = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(BuildTaskXml(exePath))];
        string xmlPath = Path.Combine(Path.GetTempPath(), $"fshw-startup-{Guid.NewGuid():N}.xml");
        try
        {
            using (var file = new FileStream(xmlPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(xml);

            using var locked = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var check = new byte[xml.Length + 1];
            int read = locked.ReadAtLeast(check, check.Length, throwOnEndOfStream: false);
            if (!check.AsSpan(0, read).SequenceEqual(xml))
            {
                Log.Write("Startup task file was changed before it could be registered.");
                return false;
            }
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
