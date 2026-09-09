using System.Drawing;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>
/// A no-op <see cref="IDisplayDevice"/> that records what it was asked to do instead of talking
/// to hardware. Used by tests (no serial port available) and by a headless dry run.
/// </summary>
public sealed class NullDisplayDevice : IDisplayDevice
{
    public NullDisplayDevice(int width = 320, int height = 480)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public int InitializeCount { get; private set; }
    public int FrameCount { get; private set; }
    public int ClearCount { get; private set; }
    public int TurnOffCount { get; private set; }
    public Size? LastFrameSize { get; private set; }

    public void Initialize() => InitializeCount++;

    public void Display(Bitmap frame)
    {
        FrameCount++;
        LastFrameSize = frame.Size;
    }

    public void Clear() => ClearCount++;

    public void TurnOff() => TurnOffCount++;

    public void Dispose() { }
}
