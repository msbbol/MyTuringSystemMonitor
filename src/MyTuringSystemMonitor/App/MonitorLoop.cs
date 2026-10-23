using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.App;

/// <summary>
/// The core render loop: read sensors, render the theme, push a frame, wait, repeat. It depends
/// only on abstractions (<see cref="IDisplayDevice"/>, <see cref="ISensorProvider"/>,
/// <see cref="IThemeRenderer"/>) so it runs identically against real hardware or test doubles.
/// If the display drops out (e.g. the laptop slept and USB lost power) it reconnects on its own.
/// </summary>
public sealed class MonitorLoop
{
    /// <summary>Wait before the second reconnect attempt; doubles up to <see cref="MaxRetryDelay"/>.</summary>
    public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private readonly IDisplayDevice _device;
    private readonly ISensorProvider _sensors;
    private readonly IThemeRenderer _renderer;
    private readonly Theme _theme;
    private readonly TimeSpan _interval;
    private readonly Action<string>? _log;
    private readonly Func<DateTime> _clock;

    private bool _displayLost;
    private DateTime _nextReconnectAt;
    private TimeSpan _retryDelay = InitialRetryDelay;
    private int _failedAttempts;

    public MonitorLoop(
        IDisplayDevice device,
        ISensorProvider sensors,
        IThemeRenderer renderer,
        Theme theme,
        TimeSpan interval,
        Action<string>? log = null,
        Func<DateTime>? clock = null)
    {
        _device = device;
        _sensors = sensors;
        _renderer = renderer;
        _theme = theme;
        _interval = interval;
        _log = log;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    private long _framesShown;
    private long _lastFrameTicks;

    /// <summary>True while the display is unreachable and the loop is trying to reconnect.</summary>
    public bool DisplayLost => _displayLost;

    /// <summary>The most recent sensor readings (for diagnostics), or null before the first read.</summary>
    public SensorSnapshot? LastSnapshot { get; private set; }

    /// <summary>Frames successfully pushed to the display since the loop was created.</summary>
    public long FramesShown => Interlocked.Read(ref _framesShown);

    /// <summary>UTC time of the last frame pushed, or null if none yet.</summary>
    public DateTime? LastFrameAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFrameTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Called with every frame after it reached the display (e.g. to keep a copy for a live
    /// preview). The bitmap is disposed right after the callback returns — clone it to keep it.
    /// </summary>
    public Action<System.Drawing.Bitmap>? FrameShown { get; init; }

    /// <summary>One-time startup: warm up sensors and open the device (brightness is set here).</summary>
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

    /// <summary>
    /// One loop iteration: reconnect if the display is down (respecting the back-off), otherwise
    /// render a frame. A failure while pushing the frame marks the display as lost.
    /// </summary>
    public void Tick()
    {
        if (_displayLost && !TryReconnect())
            return;

        SensorSnapshot snapshot;
        try
        {
            snapshot = _sensors.Read();
            LastSnapshot = snapshot;
        }
        catch (Exception ex)
        {
            // A sensor hiccup isn't a display problem — skip this frame, keep the connection.
            _log?.Invoke($"Sensor read failed: {ex.Message}");
            return;
        }

        try
        {
            using var frame = _renderer.Render(_theme, snapshot, _device.Width, _device.Height);
            _device.Display(frame);

            Interlocked.Increment(ref _framesShown);
            Interlocked.Exchange(ref _lastFrameTicks, _clock().Ticks);
            try { FrameShown?.Invoke(frame); }
            catch (Exception ex) { _log?.Invoke($"Frame observer failed: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            MarkDisplayLost(ex);
        }
    }

    private void MarkDisplayLost(Exception ex)
    {
        if (_displayLost) return;

        _displayLost = true;
        _failedAttempts = 0;
        _retryDelay = InitialRetryDelay;
        _nextReconnectAt = _clock();   // first attempt right away
        _log?.Invoke($"Display connection lost ({ex.Message}). Reconnecting…");
    }

    private bool TryReconnect()
    {
        var now = _clock();
        if (now < _nextReconnectAt)
            return false;

        if (_device.Reconnect())
        {
            _log?.Invoke(_failedAttempts == 0
                ? "Display reconnected."
                : $"Display reconnected after {_failedAttempts + 1} attempts.");
            _displayLost = false;
            _failedAttempts = 0;
            _retryDelay = InitialRetryDelay;
            return true;
        }

        _failedAttempts++;
        _nextReconnectAt = now + _retryDelay;
        _log?.Invoke($"Display still unavailable; retrying in {_retryDelay.TotalSeconds:F0} s.");
        _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
        return false;
    }

    /// <summary>Blanks and powers off the screen. Called on shutdown.</summary>
    public void Shutdown()
    {
        if (_displayLost) return;   // nothing to talk to
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
                Tick();

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
