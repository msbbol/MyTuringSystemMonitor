using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.App;

/// <summary>
/// Builds the pieces of a monitor from <see cref="AppConfig"/>. Shared by the console runner and
/// the Windows service so both wire things up identically.
/// </summary>
public static class MonitorFactory
{
    public static ISensorProvider CreateSensors(AppConfig config) =>
        config.SensorProvider.ToLowerInvariant() switch
        {
            "fake" => new FakeSensorProvider(),
            "perfcounter" => new PerformanceCounterSensorProvider(config.FrameRateCounter),
            _ => new LibreHardwareSensorProvider(config.FrameRateCounter),
        };

    public static Theme LoadTheme(AppConfig config) =>
        ThemeCatalog.LoadByName(AppPaths.ThemesDir, config.Theme, AppPaths.FontsDir);

    /// <summary>true/false when the theme declares an axis, null when it works either way.</summary>
    public static bool? IsLandscape(Theme theme) => theme.Orientation switch
    {
        ThemeOrientation.Landscape => true,
        ThemeOrientation.Portrait => false,
        _ => null,
    };

    public static bool IsNullDisplay(AppConfig config) =>
        string.Equals(config.DisplayProvider, "Null", StringComparison.OrdinalIgnoreCase);

    public static bool IsWifiDisplay(AppConfig config) =>
        string.Equals(config.DisplayProvider, "Esp32Wifi", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates (but does not open) the display for <paramref name="theme"/>. Throws
    /// <see cref="InvalidOperationException"/> when the COM port can't be resolved.
    /// </summary>
    public static IDisplayDevice CreateDevice(AppConfig config, Theme theme, Action<string>? log)
    {
        var landscape = IsLandscape(theme);

        if (IsNullDisplay(config))
        {
            var (w, h) = config.RenderSize(landscape);
            log?.Invoke($"Display: Null (headless dry run) at {w}x{h} — no serial port opened.");
            return new NullDisplayDevice(w, h);
        }

        if (IsWifiDisplay(config))
        {
            var options = config.ToWifiOptions(landscape);
            log?.Invoke($"Display: ESP32 Wi-Fi panel at {options.Host}, brightness {config.Brightness}%, " +
                        $"orientation {config.EffectiveOrientation(landscape)}, rendering at {options.RenderWidth}x{options.RenderHeight}");
            return new WifiDisplayDevice(options, log);
        }

        var port = ComPortResolver.Resolve(config.ComPort);
        log?.Invoke($"Display: {config.Revision} on {port}, brightness {config.Brightness}%, " +
                    $"orientation {config.EffectiveOrientation(landscape)}, reset-on-startup={config.ResetOnStartup}");
        return new TuringDisplayDevice(config.ToDisplayOptions(port, landscape), log);
    }
}
