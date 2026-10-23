using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Measures frame rate the way PresentMon does: by listening to the Windows DXGI "PresentStart"
/// trace events and counting presented frames per process. Windows has no FPS counter, so this is
/// the only way to get it without injecting into games.
/// <para>
/// Covers DirectX 9/11/12 apps (everything that presents through DXGI). Games using Vulkan or
/// OpenGL don't go through DXGI and won't be counted. The Windows compositor (dwm) presents
/// constantly and is ignored, so what's reported is the busiest rendering application.
/// </para>
/// </summary>
public sealed class FrameRateMeter : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int PresentStartEventId = 42;
    private const string SessionName = "TuringSystemMonitorFrameRate";

    /// <summary>Apps that always present but aren't what anyone means by "FPS".</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase) { "dwm" };

    private readonly object _gate = new();
    private readonly Dictionary<int, int> _presents = new();         // process id -> frames since last sample
    private readonly Dictionary<int, string> _names = new();         // process id -> name (cached; pids get reused rarely)
    private readonly double _minimumFps;

    private TraceEventSession? _session;
    private DateTime _lastSample = DateTime.UtcNow;
    private bool _started;
    private bool _unavailable;

    /// <param name="minimumFps">Below this the result is reported as "nothing is rendering".</param>
    public FrameRateMeter(double minimumFps = 5)
    {
        _minimumFps = minimumFps;
    }

    /// <summary>Frames per second of the busiest app and its name, or null when nothing qualifies.</summary>
    public (double Fps, string? App)? Sample()
    {
        if (!EnsureStarted()) return null;

        Dictionary<int, int> counts;
        double seconds;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            seconds = (now - _lastSample).TotalSeconds;
            _lastSample = now;
            counts = new Dictionary<int, int>(_presents);
            _presents.Clear();
        }

        if (seconds <= 0) return null;

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var (pid, frames) in counts)
            {
                var name = _names.TryGetValue(pid, out var cached) ? cached : ProcessName(pid);
                if (name is null) continue;
                byName[name] = byName.GetValueOrDefault(name) + frames;
            }
        }

        return Busiest(byName, seconds, _minimumFps);
    }

    /// <summary>Picks the busiest non-ignored app. Pure, so it can be unit-tested.</summary>
    public static (double Fps, string? App)? Busiest(
        IReadOnlyDictionary<string, int> framesByApp, double seconds, double minimumFps)
    {
        if (seconds <= 0) return null;

        double bestFps = 0;
        string? bestApp = null;
        foreach (var (app, frames) in framesByApp)
        {
            if (Ignored.Contains(app)) continue;
            var fps = frames / seconds;
            if (fps > bestFps) { bestFps = fps; bestApp = app; }
        }

        return bestFps >= minimumFps ? (bestFps, bestApp) : null;
    }

    private bool EnsureStarted()
    {
        if (_started) return true;
        if (_unavailable) return false;

        try
        {
            // A session from a previous run (e.g. after a crash) would keep the name taken.
            if (TraceEventSession.GetActiveSessionNames().Contains(SessionName, StringComparer.OrdinalIgnoreCase))
                new TraceEventSession(SessionName) { StopOnDispose = true }.Dispose();

            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(DxgiProvider, TraceEventLevel.Informational);
            session.Source.AllEvents += OnEvent;

            _session = session;
            _lastSample = DateTime.UtcNow;

            // Source.Process() blocks until the session stops.
            var pump = new Thread(() => { try { session.Source.Process(); } catch { /* stopped */ } })
            {
                IsBackground = true,
                Name = "FrameRateMeter",
            };
            pump.Start();

            _started = true;
            return true;
        }
        catch (Exception)
        {
            // No rights to start a trace session, or ETW unavailable: report no FPS and stop trying.
            _unavailable = true;
            _session?.Dispose();
            _session = null;
            return false;
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if ((int)e.ID != PresentStartEventId || e.ProviderGuid != DxgiProvider) return;

        lock (_gate)
        {
            _presents[e.ProcessID] = _presents.GetValueOrDefault(e.ProcessID) + 1;
            if (!_names.ContainsKey(e.ProcessID))
            {
                // ETW gives the name for some events; fall back to asking the OS.
                var name = string.IsNullOrEmpty(e.ProcessName) ? ProcessName(e.ProcessID) : e.ProcessName;
                if (name is not null) _names[e.ProcessID] = name;
            }
        }
    }

    private static string? ProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;   // already exited
        }
    }

    public void Dispose()
    {
        try { _session?.Dispose(); } catch { /* already gone */ }
        _session = null;
        _started = false;
    }
}
