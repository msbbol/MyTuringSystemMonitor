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

    /// <summary>
    /// Measure frame rate (the <c>fps</c> sensor). Keeps a Windows trace session open while the
    /// monitor runs; turn it off if you don't use the FPS readout.
    /// </summary>
    public bool FrameRateCounter { get; set; } = true;

    /// <summary>
    /// "Turing" (the real serial display), "Esp32Wifi" (ESP32-4848S040 panel over Wi-Fi) or
    /// "Null" (headless dry run: renders frames, no port).
    /// </summary>
    public string DisplayProvider { get; set; } = "Turing";

    /// <summary>
    /// Address of the ESP32 Wi-Fi panel: "AUTO" (find it on the local network), a hostname such as
    /// <c>turing-esp32-a1b2.local</c>, or an IP address. Only used with <see cref="DisplayProvider"/> "Esp32Wifi".
    /// </summary>
    public string WifiHost { get; set; } = WifiDisplayDevice.Auto;

    /// <summary>
    /// Extra clockwise rotation of the ESP32 panel in quarter turns (0–3), for however it's
    /// mounted. The "Reverse…" orientations add a half turn on top.
    /// </summary>
    public int WifiRotation { get; set; }

    /// <summary>Theme name (folder name for YAML themes, file name without extension for JSON themes).</summary>
    public string? Theme { get; set; } = "default";

    /// <summary>
    /// Port of the control web UI hosted by the Windows service. It only listens on localhost.
    /// Changing it takes effect after the service restarts.
    /// </summary>
    public int WebPort { get; set; } = DefaultWebPort;

    public const int DefaultWebPort = 5321;

    /// <summary>Fallback frame size used only if the driver reports a 0-size screen.</summary>
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 480;

    /// <summary>
    /// Rotates <paramref name="configured"/> onto the theme's axis while keeping the user's
    /// reverse-variant preference (e.g. ReversePortrait + landscape theme -> ReverseLandscape).
    /// </summary>
    public static string AlignOrientation(string configured, bool themeIsLandscape)
    {
        var reversed = configured.StartsWith("Reverse", StringComparison.OrdinalIgnoreCase);
        return themeIsLandscape
            ? (reversed ? "ReverseLandscape" : "Landscape")
            : (reversed ? "ReversePortrait" : "Portrait");
    }

    /// <summary>
    /// The orientation to actually drive the panel with. A theme that declares an axis wins, so
    /// picking a landscape theme rotates the screen automatically.
    /// </summary>
    public string EffectiveOrientation(bool? themeIsLandscape = null) =>
        themeIsLandscape is null ? Orientation : AlignOrientation(Orientation, themeIsLandscape.Value);

    /// <summary>
    /// Canvas size for the effective orientation: portrait keeps the smaller edge as width,
    /// landscape swaps so width is the larger edge (e.g. 320x480 -> 480x320).
    /// </summary>
    public (int Width, int Height) RenderSize(bool? themeIsLandscape = null)
    {
        var lo = Math.Min(Width, Height);
        var hi = Math.Max(Width, Height);
        var orientation = EffectiveOrientation(themeIsLandscape);
        var landscape = orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase)
                        || orientation.Equals("ReverseLandscape", StringComparison.OrdinalIgnoreCase);
        return landscape ? (hi, lo) : (lo, hi);
    }

    public DisplayDeviceOptions ToDisplayOptions(string resolvedPort, bool? themeIsLandscape = null) => new()
    {
        Revision = Enum.TryParse<DisplayRevision>(Revision, ignoreCase: true, out var rev) ? rev : DisplayRevision.RevisionA,
        PortName = resolvedPort,
        ConfiguredPort = ComPort,
        Orientation = Enum.TryParse<DisplayOrientation>(EffectiveOrientation(themeIsLandscape), ignoreCase: true, out var o) ? o : DisplayOrientation.Portrait,
        BrightnessPercent = Brightness,
        ResetOnStartup = ResetOnStartup,
        FallbackWidth = Width,
        FallbackHeight = Height,
    };

    public WifiDisplayOptions ToWifiOptions(bool? themeIsLandscape = null)
    {
        var (w, h) = RenderSize(themeIsLandscape);
        var reversed = EffectiveOrientation(themeIsLandscape).StartsWith("Reverse", StringComparison.OrdinalIgnoreCase);
        return new WifiDisplayOptions
        {
            Host = string.IsNullOrWhiteSpace(WifiHost) ? WifiDisplayDevice.Auto : WifiHost.Trim(),
            RenderWidth = w,
            RenderHeight = h,
            QuarterTurns = (((WifiRotation + (reversed ? 2 : 0)) % 4) + 4) % 4,
            BrightnessPercent = Brightness,
        };
    }
}
