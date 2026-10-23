using System.Reflection;

namespace MyTuringSystemMonitor.Service;

public static class ServiceInfo
{
    /// <summary>Windows service name — keep in sync with install/install.ps1 and the tray app.</summary>
    public const string Name = "MyTuringSystemMonitor";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
