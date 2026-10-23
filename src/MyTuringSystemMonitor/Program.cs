using System.Drawing.Imaging;
using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

var baseDir = AppContext.BaseDirectory;

void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

var calibrate = args.Any(a =>
    a.Equals("calibrate", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--calibrate", StringComparison.OrdinalIgnoreCase));

var preview = args.Any(a =>
    a.Equals("preview", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--preview", StringComparison.OrdinalIgnoreCase));

AppPaths.EnsureSeeded(baseDir);
var config = ConfigLoader.LoadOrCreate(AppPaths.ConfigPath);

// --- Preview mode --------------------------------------------------------------------------
// `dotnet run --project src/MyTuringSystemMonitor -- preview` renders the configured theme with
// sample data to theme-preview.png and exits — no hardware, no admin. Handy for tuning layout.
if (preview)
{
    var previewTheme = ThemeCatalog.LoadByName(AppPaths.ThemesDir, config.Theme, AppPaths.FontsDir);
    using var sampleSensors = new FakeSensorProvider();
    sampleSensors.Initialize();
    var snapshot = sampleSensors.Read();
    var (pw, ph) = config.RenderSize(MonitorFactory.IsLandscape(previewTheme));
    using var frame = new GdiThemeRenderer().Render(previewTheme, snapshot, pw, ph);
    var previewPath = Path.Combine(baseDir, "theme-preview.png");
    frame.Save(previewPath, ImageFormat.Png);
    Log($"Rendered theme '{previewTheme.Name}' at {pw}x{ph} to {previewPath}.");
    return 0;
}

// --- Sensors -------------------------------------------------------------------------------
var sensors = MonitorFactory.CreateSensors(config);
Log($"Sensor provider: {sensors.GetType().Name}");

// --- Theme ---------------------------------------------------------------------------------
var theme = MonitorFactory.LoadTheme(config);

// A theme designed for a given axis rotates the panel automatically; the user's reverse-variant
// preference (e.g. ReversePortrait) is preserved.
var themeLandscape = MonitorFactory.IsLandscape(theme);
Log($"Theme: {theme.Name} ({theme.Widgets.Count} widgets), orientation {config.EffectiveOrientation(themeLandscape)}" +
    (themeLandscape is null ? " (from config)" : " (from theme)"));

// --- Display -------------------------------------------------------------------------------
IDisplayDevice device;
try
{
    device = MonitorFactory.CreateDevice(config, theme, Log);
}
catch (Exception ex)
{
    Log(ex.Message);
    sensors.Dispose();
    return 1;
}

// --- Calibration mode ----------------------------------------------------------------------
// `dotnet run --project src/MyTuringSystemMonitor -- calibrate` pushes one diagnostic frame so
// we can see exactly how the image maps to the panel (aspect / shear / orientation).
if (calibrate)
{
    try
    {
        device.Initialize();
        using var pattern = CalibrationImage.Create(device.Width, device.Height);
        var pngPath = Path.Combine(baseDir, "calibration.png");
        pattern.Save(pngPath, ImageFormat.Png);
        device.Display(pattern);
        Log($"Calibration frame sent ({device.Width}x{device.Height}). Reference image saved to {pngPath}.");
        Log("Compare the panel to that image, then press Enter to exit.");
        Console.ReadLine();
    }
    catch (Exception ex)
    {
        Log($"Calibration failed: {ex.Message}");
    }
    finally
    {
        device.Clear();
        device.TurnOff();
        device.Dispose();
        sensors.Dispose();
    }
    return 0;
}

var renderer = new GdiThemeRenderer();
var loop = new MonitorLoop(device, sensors, renderer, theme,
    TimeSpan.FromMilliseconds(config.RefreshIntervalMs), Log);

// --- Run -----------------------------------------------------------------------------------
using var cts = new CancellationTokenSource();

// Ctrl+C and process exit both request a clean stop. Guard against a second call and against
// the source already being disposed (ProcessExit can fire after the using-block disposed it).
void RequestStop()
{
    try
    {
        if (!cts.IsCancellationRequested)
            cts.Cancel();
    }
    catch (ObjectDisposedException) { /* already shutting down */ }
}

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;      // let us shut down cleanly instead of hard-killing
    Log("Stopping…");
    RequestStop();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestStop();

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
