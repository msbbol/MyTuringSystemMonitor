using System.Drawing;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>
/// App-level abstraction over the physical display. Nothing above this interface touches
/// <c>TuringSmartScreenLib</c> directly, which keeps the rendering/loop layers testable with a
/// fake device (see <see cref="NullDisplayDevice"/>) since there is no serial port in CI.
/// </summary>
public interface IDisplayDevice : IDisposable
{
    /// <summary>Frame width in pixels for the current orientation.</summary>
    int Width { get; }

    /// <summary>Frame height in pixels for the current orientation.</summary>
    int Height { get; }

    /// <summary>
    /// Opens the device and applies one-time setup: orientation and brightness are set once here.
    /// Whether the device is reset first is a constructor concern (off by default — see
    /// <see cref="TuringDisplayDevice"/>).
    /// </summary>
    void Initialize();

    /// <summary>Pushes a full frame to the screen. The bitmap should be <see cref="Width"/> x <see cref="Height"/>.</summary>
    void Display(Bitmap frame);

    /// <summary>Blanks the screen (used on shutdown).</summary>
    void Clear();

    /// <summary>Turns the backlight/panel off (used on shutdown).</summary>
    void TurnOff();
}
