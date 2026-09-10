using System.Windows.Forms;
using MyTuringSystemMonitor.Configuration;

namespace MyTuringSystemMonitor.Config;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var log = Path.Combine(AppPaths.DataDir, "config-app.log");
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(log, $"{DateTime.Now:O} start\n");

            // Seed shared config/themes from the files bundled with this app on first run.
            AppPaths.EnsureSeeded(AppContext.BaseDirectory);

            ApplicationConfiguration.Initialize();
            File.AppendAllText(log, $"{DateTime.Now:O} run\n");
            Application.Run(new MainForm());
            File.AppendAllText(log, $"{DateTime.Now:O} exit ok\n");
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(log, $"{DateTime.Now:O} FATAL {ex}\n"); } catch { /* ignore */ }
            throw;
        }
    }
}
