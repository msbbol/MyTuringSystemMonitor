using MyTuringSystemMonitor.Hardware;

namespace MyTuringSystemMonitor.Configuration;

/// <summary>
/// User settings, loaded from <c>config.json</c>. Defaults mirror the confirmed-working baseline
/// (COM4, 20% brightness, portrait, no reset on startup).
/// </summary>
public sealed class AppConfig
{
    /// <summary>COM port, or "AUTO" to auto-detect (see <see cref="ComPortResolver"/>).</summary>
    public string ComPort { get; set; } = "COM4";

    /// <summary>Screen protocol; "RevisionA" for the 3.5" model.</summary>
    public string Revision { get; set; } = "RevisionA";

    /// <summary>Brightness 0–100 (%). Baseline: 20.</summary>
    public int Brightness { get; set; } = 20;

    public string Orientation { get; set; } = "Portrait";

    /// <summary>
    /// Keep FALSE. Resetting on startup makes the device re-enumerate and change COM port,
    /// which silently breaks it. See README and <see cref="DisplayDeviceOptions.ResetOnStartup"/>.
    /// </summary>
    public bool ResetOnStartup { get; set; }

    public int RefreshIntervalMs { get; set; } = 1000;

    /// <summary>"Lhm" (full sensors, needs admin), "PerfCounter" (CPU/RAM, no admin), or "Fake" (dry run).</summary>
    public string SensorProvider { get; set; } = "Lhm";

    /// <summary>"Turing" (the real serial display) or "Null" (headless dry run: renders frames, no port).</summary>
    public string DisplayProvider { get; set; } = "Turing";

    /// <summary>Path to a theme JSON file (relative to the app dir). Falls back to the built-in theme if missing.</summary>
    public string? Theme { get; set; } = "themes/default.json";

    /// <summary>Fallback frame size used only if the driver reports a 0-size screen.</summary>
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 480;

    public DisplayDeviceOptions ToDisplayOptions(string resolvedPort) => new()
    {
        Revision = Enum.TryParse<DisplayRevision>(Revision, ignoreCase: true, out var rev) ? rev : DisplayRevision.RevisionA,
        PortName = resolvedPort,
        Orientation = Enum.TryParse<DisplayOrientation>(Orientation, ignoreCase: true, out var o) ? o : DisplayOrientation.Portrait,
        BrightnessPercent = Brightness,
        ResetOnStartup = ResetOnStartup,
        FallbackWidth = Width,
        FallbackHeight = Height,
    };
}
