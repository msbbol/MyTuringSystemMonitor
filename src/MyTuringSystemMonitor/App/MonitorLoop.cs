using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.App;

/// <summary>
/// The core render loop: read sensors, render the theme, push a frame, wait, repeat. It depends
/// only on abstractions (<see cref="IDisplayDevice"/>, <see cref="ISensorProvider"/>,
/// <see cref="IThemeRenderer"/>) so it runs identically against real hardware or test doubles.
/// </summary>
public sealed class MonitorLoop
{
    private readonly IDisplayDevice _device;
    private readonly ISensorProvider _sensors;
    private readonly IThemeRenderer _renderer;
    private readonly Theme _theme;
    private readonly TimeSpan _interval;
    private readonly Action<string>? _log;

    public MonitorLoop(
        IDisplayDevice device,
        ISensorProvider sensors,
        IThemeRenderer renderer,
        Theme theme,
        TimeSpan interval,
        Action<string>? log = null)
    {
        _device = device;
        _sensors = sensors;
        _renderer = renderer;
        _theme = theme;
        _interval = interval;
        _log = log;
    }

    /// <summary>One-time startup: warm up sensors and open the device (brightness is set here, once).</summary>
    public void Start()
    {
        _sensors.Initialize();
        _device.Initialize();
    }

    /// <summary>Reads sensors and pushes exactly one frame. Kept separate so it is unit-testable.</summary>
    public void RenderOnce()
    {
        var snapshot = _sensors.Read();
        using var frame = _renderer.Render(_theme, snapshot, _device.Width, _device.Height);
        _device.Display(frame);
    }

    /// <summary>Blanks and powers off the screen. Called on shutdown.</summary>
    public void Shutdown()
    {
        try
        {
            _device.Clear();
            _device.TurnOff();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Shutdown cleanup failed: {ex.Message}");
        }
    }

    /// <summary>Runs until <paramref name="cancellationToken"/> is signalled, then cleans up.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Start();
        _log?.Invoke($"Running at {_interval.TotalMilliseconds:F0} ms interval on a {_device.Width}x{_device.Height} screen. Ctrl+C to stop.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    RenderOnce();
                }
                catch (Exception ex)
                {
                    // A transient sensor/serial hiccup shouldn't kill the loop.
                    _log?.Invoke($"Frame failed: {ex.Message}");
                }

                try { await Task.Delay(_interval, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            Shutdown();
        }
    }
}
