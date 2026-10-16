using System.IO;

namespace FSHWMonitor.Core;

/// <summary>Tiny append-only log in the settings folder, handy for bug reports.</summary>
public static class Log
{
    static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppSettings.Folder, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 512 * 1024)
                    file.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the dashboard down.
        }
    }
}
