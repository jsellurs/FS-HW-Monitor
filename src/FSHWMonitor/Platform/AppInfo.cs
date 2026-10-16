using System.Reflection;

namespace FSHWMonitor.Platform;

public static class AppInfo
{
    public const string Name = "FS HW Monitor";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "1.0.0";
}
