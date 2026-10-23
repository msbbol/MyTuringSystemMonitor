using System.Windows.Forms;

namespace MyTuringSystemMonitor.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One tray icon per user session, however it was launched (autostart, installer, by hand).
        using var mutex = new Mutex(initiallyOwned: true, @"Local\MyTuringSystemMonitor.Tray", out var createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
