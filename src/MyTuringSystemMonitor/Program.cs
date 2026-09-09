using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

var baseDir = AppContext.BaseDirectory;

void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

var config = ConfigLoader.LoadOrCreate(Path.Combine(baseDir, "config.json"));

// --- Sensors -------------------------------------------------------------------------------
ISensorProvider sensors = config.SensorProvider.ToLowerInvariant() switch
{
    "fake" => new FakeSensorProvider(),
    "perfcounter" => new PerformanceCounterSensorProvider(),
    _ => new LibreHardwareSensorProvider(),
};
Log($"Sensor provider: {sensors.GetType().Name}");

// --- Theme ---------------------------------------------------------------------------------
var themePath = string.IsNullOrWhiteSpace(config.Theme) ? null : Path.Combine(baseDir, config.Theme);
var theme = ThemeLoader.LoadOrDefault(themePath);
Log($"Theme: {theme.Name} ({theme.Widgets.Count} widgets)");

// --- Display -------------------------------------------------------------------------------
IDisplayDevice device;
if (string.Equals(config.DisplayProvider, "Null", StringComparison.OrdinalIgnoreCase))
{
    Log($"Display: Null (headless dry run) at {config.Width}x{config.Height} — no serial port opened.");
    device = new NullDisplayDevice(config.Width, config.Height);
}
else
{
    string port;
    try
    {
        port = ComPortResolver.Resolve(config.ComPort);
    }
    catch (Exception ex)
    {
        Log(ex.Message);
        sensors.Dispose();
        return 1;
    }
    Log($"Display: {config.Revision} on {port}, brightness {config.Brightness}%, reset-on-startup={config.ResetOnStartup}");
    device = new TuringDisplayDevice(config.ToDisplayOptions(port));
}
var renderer = new GdiThemeRenderer();
var loop = new MonitorLoop(device, sensors, renderer, theme,
    TimeSpan.FromMilliseconds(config.RefreshIntervalMs), Log);

// --- Run -----------------------------------------------------------------------------------
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;      // let us shut down cleanly instead of hard-killing
    Log("Stopping…");
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

try
{
    await loop.RunAsync(cts.Token);
}
catch (Exception ex)
{
    Log($"Fatal: {ex.Message}");
    return 1;
}
finally
{
    device.Dispose();
    sensors.Dispose();
}

Log("Stopped.");
return 0;
