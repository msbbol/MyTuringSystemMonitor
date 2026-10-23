namespace MyTuringSystemMonitor.Hardware;

/// <summary>Screen protocol/model. Mirrors TuringSmartScreenLib's ScreenType; RevisionA = 3.5".</summary>
public enum DisplayRevision
{
    RevisionA,
    RevisionB,
    RevisionC,
    RevisionE,
}

/// <summary>Panel orientation. Mirrors TuringSmartScreenLib's ScreenOrientation.</summary>
public enum DisplayOrientation
{
    Portrait,
    ReversePortrait,
    Landscape,
    ReverseLandscape,
}

/// <summary>Everything <see cref="TuringDisplayDevice"/> needs to open and configure the screen.</summary>
public sealed record DisplayDeviceOptions
{
    public DisplayRevision Revision { get; init; } = DisplayRevision.RevisionA;
    public string PortName { get; init; } = "COM4";

    /// <summary>
    /// The port as written in config ("COM4" or "AUTO"), used to re-find the device when it
    /// re-enumerates after sleep. Null means "same as <see cref="PortName"/>".
    /// </summary>
    public string? ConfiguredPort { get; init; }
    public DisplayOrientation Orientation { get; init; } = DisplayOrientation.Portrait;

    /// <summary>Brightness 0–100 (%), applied once at startup.</summary>
    public int BrightnessPercent { get; init; } = 20;

    /// <summary>
    /// Reset the device on startup. LEAVE THIS FALSE. Resetting makes the device drop off USB and
    /// re-enumerate, which can change its COM port number and then silently fail against the
    /// configured port. This was the original "the screen doesn't work" root cause — see README.
    /// </summary>
    public bool ResetOnStartup { get; init; }

    /// <summary>Used when the driver reports a 0-size screen (fallback to the 3.5" native 320x480).</summary>
    public int FallbackWidth { get; init; } = 320;
    public int FallbackHeight { get; init; } = 480;
}

/// <summary>Everything <see cref="WifiDisplayDevice"/> needs to reach the ESP32 panel over the network.</summary>
public sealed record WifiDisplayOptions
{
    /// <summary>"AUTO" (find the panel with a UDP broadcast), a hostname such as <c>turing-esp32-a1b2.local</c>, or an IP.</summary>
    public string Host { get; init; } = WifiDisplayDevice.Auto;
    public int Port { get; init; } = WifiDisplayDevice.DefaultPort;
    public int DiscoveryPort { get; init; } = WifiDisplayDevice.DefaultDiscoveryPort;

    /// <summary>
    /// Size the theme is rendered at (e.g. 320x480). Frames that don't match the panel are scaled
    /// to fit and centred, so the 3.5" themes show unchanged on the square 480x480 panel.
    /// </summary>
    public int RenderWidth { get; init; } = 320;
    public int RenderHeight { get; init; } = 480;

    /// <summary>Clockwise rotation of the panel in quarter turns (0–3), applied on the device.</summary>
    public int QuarterTurns { get; init; }

    /// <summary>Brightness 0–100 (%), applied whenever the connection is (re)established.</summary>
    public int BrightnessPercent { get; init; } = 20;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait for the panel to confirm a frame before treating it as gone.</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
