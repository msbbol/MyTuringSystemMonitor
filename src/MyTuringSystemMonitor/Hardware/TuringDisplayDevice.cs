using System.Drawing;
using TuringSmartScreenLib;
using TuringSmartScreenLib.Helpers.GdiPlus;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>
/// The real display, driven over serial by <c>TuringSmartScreenLib</c>. This is the ONLY type in
/// the app that references the serial library — everything else goes through <see cref="IDisplayDevice"/>.
/// </summary>
public sealed class TuringDisplayDevice : IDisplayDevice
{
    private readonly DisplayDeviceOptions _options;
    private readonly Action<string>? _log;
    private IScreen? _screen;
    private string _portName;

    public TuringDisplayDevice(DisplayDeviceOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log;
        _portName = options.PortName;
        Width = options.FallbackWidth;
        Height = options.FallbackHeight;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The COM port currently in use (can change after a reconnect).</summary>
    public string PortName => _portName;

    public void Initialize() => Open(isReconnect: false);

    public bool Reconnect()
    {
        CloseQuietly();

        // After sleep/wake the device re-enumerates and may come back on a different COM number.
        var port = ComPortResolver.ResolveForReconnect(_options.ConfiguredPort ?? _options.PortName, _portName);
        if (!string.Equals(port, _portName, StringComparison.OrdinalIgnoreCase))
            _log?.Invoke($"Display moved from {_portName} to {port}.");
        _portName = port;

        try
        {
            Open(isReconnect: true);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Reconnect to {_portName} failed: {ex.Message}");
            CloseQuietly();
            return false;
        }
    }

    private void Open(bool isReconnect)
    {
        var screen = ScreenFactory.Create(MapType(_options.Revision), _portName);
        try
        {
            // ---------------------------------------------------------------------------------
            // DO NOT reset on startup by default. Reset() makes the device disconnect and
            // re-enumerate on USB; its COM port number can change, after which every write
            // silently fails against the old port. This is the one hard-won fix from the original
            // debugging session — keep it opt-in via config only, and never on a reconnect (that
            // would knock the device straight back off the bus). See README / DisplayDeviceOptions.
            // ---------------------------------------------------------------------------------
            if (_options.ResetOnStartup && !isReconnect)
            {
                screen.Reset();
                Thread.Sleep(500); // let it re-enumerate before we talk to it again
            }

            // Wake the panel: a previous run's clean shutdown calls ScreenOff(), and a device that
            // lost power during sleep also comes back dark. ScreenOn() is not a reset, so it doesn't
            // trigger the re-enumeration problem above.
            screen.ScreenOn();

            screen.Orientation = MapOrientation(_options.Orientation);
            screen.Clear();

            // Brightness is applied whenever the connection is (re)established — a power-cycled
            // panel forgets it.
            screen.SetBrightness((byte)Math.Clamp(_options.BrightnessPercent, 0, 100));

            // Trust the driver's reported size, but fall back to the native 320x480 if it reports 0.
            Width = screen.Width > 0 ? screen.Width : _options.FallbackWidth;
            Height = screen.Height > 0 ? screen.Height : _options.FallbackHeight;

            _log?.Invoke($"Driver reports {screen.Width}x{screen.Height} for orientation {_options.Orientation} on {_portName}; rendering at {Width}x{Height}.");
        }
        catch
        {
            try { screen.Dispose(); } catch { /* port already gone */ }
            throw;
        }

        _screen = screen;
    }

    /// <summary>Releases the serial handle even if the device has already vanished.</summary>
    private void CloseQuietly()
    {
        var screen = _screen;
        _screen = null;
        if (screen is null) return;
        try { screen.Dispose(); } catch { /* the port died underneath us — nothing to release */ }
    }

    public void Display(Bitmap frame)
    {
        var screen = _screen ?? throw new InvalidOperationException("Initialize() must be called before Display().");

        var buffer = screen.CreateBufferFrom(frame);
        try
        {
            screen.DisplayBuffer(0, 0, buffer);
        }
        finally
        {
            // IScreenBuffer isn't IDisposable at the interface level, but the concrete buffers are;
            // dispose to avoid leaking native memory each frame.
            if (buffer is IDisposable disposable)
                disposable.Dispose();
        }
    }

    public void Clear() => _screen?.Clear();

    public void TurnOff() => _screen?.ScreenOff();

    public void Dispose() => CloseQuietly();

    private static ScreenType MapType(DisplayRevision revision) => revision switch
    {
        DisplayRevision.RevisionA => ScreenType.RevisionA,
        DisplayRevision.RevisionB => ScreenType.RevisionB,
        DisplayRevision.RevisionC => ScreenType.RevisionC,
        DisplayRevision.RevisionE => ScreenType.RevisionE,
        _ => ScreenType.RevisionA,
    };

    private static ScreenOrientation MapOrientation(DisplayOrientation orientation) => orientation switch
    {
        DisplayOrientation.Portrait => ScreenOrientation.Portrait,
        DisplayOrientation.ReversePortrait => ScreenOrientation.ReversePortrait,
        DisplayOrientation.Landscape => ScreenOrientation.Landscape,
        DisplayOrientation.ReverseLandscape => ScreenOrientation.ReverseLandscape,
        _ => ScreenOrientation.Portrait,
    };
}
