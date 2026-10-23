using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace MyTuringSystemMonitor.Tray;

/// <summary>Starts/stops the Windows service, asking for elevation (UAC) when needed.</summary>
public static class ServiceControl
{
    /// <summary>Must match ServiceInfo.Name in the service and install/install.ps1.</summary>
    public const string ServiceName = "MyTuringSystemMonitor";

    /// <summary>The service status, or null if it isn't installed.</summary>
    public static ServiceControllerStatus? Query()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static void Start() => Run("Start-Service", controller => controller.Start());

    public static void Stop() => Run("Stop-Service -Force", controller => controller.Stop());

    public static void Restart() => Run("Restart-Service -Force", null);

    private static void Run(string powerShellCommand, Action<ServiceController>? direct)
    {
        if (direct is not null)
        {
            try
            {
                using var controller = new ServiceController(ServiceName);
                direct(controller);
                return;
            }
            catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
            {
                // Access denied — fall through to an elevated attempt.
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{powerShellCommand} -Name '{ServiceName}'\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user declined the UAC prompt.
        }
    }
}
