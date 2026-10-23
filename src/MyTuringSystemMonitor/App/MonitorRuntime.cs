using System.Drawing;
using System.Drawing.Imaging;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.App;

public enum MonitorState
{
    /// <summary>Not running and not asked to.</summary>
    Stopped,

    /// <summary>Paused by the user: the screen is off until resumed.</summary>
    Paused,

    /// <summary>Opening the display / waiting for the first frame.</summary>
    Starting,

    Running,

    /// <summary>The display dropped out (e.g. laptop sleep) and the loop is reconnecting.</summary>
    Reconnecting,

    /// <summary>Couldn't start (e.g. COM port missing at boot); retrying periodically.</summary>
    Faulted,
}

public sealed record MonitorStatus(
    MonitorState State,
    string? Theme,
    string? Orientation,
    int Width,
    int Height,
    string? Port,
    string DisplayProvider,
    string SensorProvider,
    int RefreshIntervalMs,
    long FramesShown,
    DateTime? LastFrameAt,
    DateTime StartedAt,
    string? LastError);

/// <summary>
/// A long-lived, controllable monitor: owns sensors, display and render loop, and can be started,
/// paused, resumed and restarted (e.g. after the config changed) from another thread — the web
/// API and tray talk to this. If the display can't be opened it keeps retrying in the background.
/// </summary>
public sealed class MonitorRuntime : IAsyncDisposable
{
    public static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(10);

    private readonly Func<AppConfig> _loadConfig;
    private readonly LogBuffer _log;
    private readonly Func<AppConfig, ISensorProvider> _sensorFactory;
    private readonly Func<AppConfig, Theme> _themeLoader;
    private readonly Func<AppConfig, Theme, Action<string>, IDisplayDevice> _deviceFactory;
    private readonly TimeSpan _startRetryDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _frameGate = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;

    private ISensorProvider? _sensors;
    private string? _sensorKind;
    private CancellationTokenSource? _cts;
    private Task? _supervisor;
    private volatile MonitorLoop? _loop;
    private volatile IDisplayDevice? _device;
    private volatile Theme? _theme;
    private AppConfig _config;
    private volatile bool _paused;
    private volatile bool _faulted;
    private volatile string? _lastError;
    private Bitmap? _lastFrame;

    public MonitorRuntime(
        Func<AppConfig> loadConfig,
        LogBuffer log,
        Func<AppConfig, ISensorProvider>? sensorFactory = null,
        Func<AppConfig, Theme>? themeLoader = null,
        Func<AppConfig, Theme, Action<string>, IDisplayDevice>? deviceFactory = null,
        TimeSpan? startRetryDelay = null)
    {
        _loadConfig = loadConfig;
        _log = log;
        _sensorFactory = sensorFactory ?? MonitorFactory.CreateSensors;
        _themeLoader = themeLoader ?? MonitorFactory.LoadTheme;
        _deviceFactory = deviceFactory ?? MonitorFactory.CreateDevice;
        _startRetryDelay = startRetryDelay ?? StartRetryDelay;
        _config = new AppConfig();
    }

    public LogBuffer Log => _log;

    /// <summary>Starts (or resumes) the monitor with the current config. No-op if already running.</summary>
    public async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _paused = false;
            if (_supervisor is { IsCompleted: false }) return;
            StartCore();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Stops pushing frames and turns the screen off until <see cref="ResumeAsync"/>.</summary>
    public async Task PauseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            _paused = true;
            _log.Write("Monitor paused.");
        }
        finally { _gate.Release(); }
    }

    public Task ResumeAsync() => StartAsync();

    /// <summary>Stops and starts again, re-reading the config (theme, port, brightness…).</summary>
    public async Task RestartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            _paused = false;
            _log.Write("Restarting monitor…");
            StartCore();
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    public MonitorStatus Status
    {
        get
        {
            var config = _config;
            var loop = _loop;
            var device = _device;
            var theme = _theme;

            MonitorState state;
            if (_paused) state = MonitorState.Paused;
            else if (_supervisor is null || _supervisor.IsCompleted) state = MonitorState.Stopped;
            else if (_faulted) state = MonitorState.Faulted;
            else if (loop is null) state = MonitorState.Starting;
            else if (loop.DisplayLost) state = MonitorState.Reconnecting;
            else state = loop.FramesShown > 0 ? MonitorState.Running : MonitorState.Starting;

            var landscape = theme is null ? (bool?)null : MonitorFactory.IsLandscape(theme);
            return new MonitorStatus(
                state,
                theme?.Name,
                theme is null ? null : config.EffectiveOrientation(landscape),
                device?.Width ?? 0,
                device?.Height ?? 0,
                device switch
                {
                    TuringDisplayDevice turing => turing.PortName,
                    WifiDisplayDevice wifi => wifi.Endpoint,
                    _ => null,
                },
                config.DisplayProvider,
                config.SensorProvider,
                config.RefreshIntervalMs,
                loop?.FramesShown ?? 0,
                loop?.LastFrameAt,
                _startedAt,
                _lastError);
        }
    }

    /// <summary>The latest sensor readings while the monitor runs, or null.</summary>
    public SensorSnapshot? LastSnapshot => _loop?.LastSnapshot;

    /// <summary>The sensor source in use, for diagnostics that need to ask it directly.</summary>
    public ISensorProvider? Sensors => _sensors;

    /// <summary>The last frame sent to the display, PNG-encoded, or null if none yet.</summary>
    public byte[]? LastFramePng()
    {
        lock (_frameGate)
        {
            if (_lastFrame is null) return null;
            using var ms = new MemoryStream();
            _lastFrame.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    private void StartCore()
    {
        _config = _loadConfig();
        _faulted = false;
        _lastError = null;

        EnsureSensors(_config);

        _cts = new CancellationTokenSource();
        var config = _config;
        var token = _cts.Token;
        _supervisor = Task.Run(() => SuperviseAsync(config, token));
    }

    private void EnsureSensors(AppConfig config)
    {
        var kind = config.SensorProvider.ToLowerInvariant();
        if (_sensors is not null && _sensorKind == kind) return;

        _sensors?.Dispose();
        _sensors = _sensorFactory(config);
        _sensorKind = kind;
        _log.Write($"Sensor provider: {_sensors.GetType().Name}");
    }

    private async Task SuperviseAsync(AppConfig config, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IDisplayDevice? device = null;
            try
            {
                var theme = _themeLoader(config);
                _theme = theme;
                device = _deviceFactory(config, theme, _log.Write);
                _device = device;

                var loop = new MonitorLoop(device, _sensors!, new GdiThemeRenderer(), theme,
                    TimeSpan.FromMilliseconds(Math.Max(100, config.RefreshIntervalMs)), _log.Write)
                {
                    FrameShown = KeepFrame,
                };

                _faulted = false;
                _loop = loop;
                _log.Write($"Theme: {theme.Name} ({theme.Widgets.Count} widgets)");
                await loop.RunAsync(token);
                return;   // cancelled cleanly
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _faulted = true;
                _lastError = ex.Message;
                _log.Write($"Monitor couldn't start: {ex.Message} Retrying in {_startRetryDelay.TotalSeconds:F0} s.");
                try { await Task.Delay(_startRetryDelay, token); }
                catch (OperationCanceledException) { return; }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                _loop = null;
                _device = null;
                try { device?.Dispose(); } catch { /* port already gone */ }
            }
        }
    }

    private void KeepFrame(Bitmap frame)
    {
        var copy = new Bitmap(frame);
        lock (_frameGate)
        {
            _lastFrame?.Dispose();
            _lastFrame = copy;
        }
    }

    private async Task StopCoreAsync()
    {
        var cts = _cts;
        var supervisor = _supervisor;
        if (cts is null || supervisor is null) return;

        cts.Cancel();
        try { await supervisor.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _log.Write($"Stop: {ex.Message}"); }
        catch (OperationCanceledException) { }

        cts.Dispose();
        _cts = null;
        _supervisor = null;
        _faulted = false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _sensors?.Dispose();
        _sensors = null;
        lock (_frameGate)
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
        }
        _gate.Dispose();
    }
}
