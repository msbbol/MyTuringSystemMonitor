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
    private IScreen? _screen;

    public TuringDisplayDevice(DisplayDeviceOptions options)
    {
        _options = options;
        Width = options.FallbackWidth;
        Height = options.FallbackHeight;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    public void Initialize()
    {
        var screen = ScreenFactory.Create(MapType(_options.Revision), _options.PortName);

        // ---------------------------------------------------------------------------------
        // DO NOT reset on startup by default. Reset() makes the device disconnect and
        // re-enumerate on USB; its COM port number can change, after which every write
        // silently fails against the old port. This is the one hard-won fix from the original
        // debugging session — keep it opt-in via config only. See README / DisplayDeviceOptions.
        // ---------------------------------------------------------------------------------
        if (_options.ResetOnStartup)
        {
            screen.Reset();
            Thread.Sleep(500); // let it re-enumerate before we talk to it again
        }

        screen.Orientation = MapOrientation(_options.Orientation);
        screen.Clear();

        // Brightness is set exactly once, here at startup.
        screen.SetBrightness((byte)Math.Clamp(_options.BrightnessPercent, 0, 100));

        // Trust the driver's reported size, but fall back to the native 320x480 if it reports 0.
        Width = screen.Width > 0 ? screen.Width : _options.FallbackWidth;
        Height = screen.Height > 0 ? screen.Height : _options.FallbackHeight;

        _screen = screen;
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

    public void Dispose()
    {
        _screen?.Dispose();
        _screen = null;
    }

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
