using System.Diagnostics;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Stateful, rate-based readings that need a previous sample: GPU utilisation, network throughput
/// and the busiest process by CPU. One instance per provider; call <see cref="Append"/> once per
/// refresh, after the provider's own readings.
/// </summary>
public sealed class SystemDynamics : IDisposable
{
    private readonly GpuEngineMeter _gpu = new();
    private readonly NetworkMeter _network = new();
    private readonly DiskIoMeter _disk = new();
    private readonly ProcessCpuMonitor _process = new();
    private readonly PeakTracker _peaks = new();
    private readonly FrameRateMeter? _frameRate;

    /// <param name="frameRate">Measure FPS (keeps an ETW trace session open while running).</param>
    public SystemDynamics(bool frameRate = true)
    {
        _frameRate = frameRate ? new FrameRateMeter() : null;
    }

    public void Append(SensorSnapshot.Builder builder)
    {
        if (_frameRate is not null)
        {
            // null while nothing is rendering — themes then show their fallback instead of "0".
            var frames = _frameRate.Sample();
            builder.Set(SensorKeys.Fps, frames?.Fps);
            builder.SetText(SensorKeys.FpsApp, frames?.App);
        }

        // Overrides a provider's own gpu.load (e.g. LHM's discrete-GPU "GPU Core", which sits at 0 %
        // on switchable-graphics laptops) whenever Windows' per-adapter counters are available.
        if (_gpu.Sample() is { } gpuLoad)
            builder.Set(SensorKeys.GpuLoad, gpuLoad);

        var (down, up, interfaces) = _network.Sample();
        _peaks.Publish(builder, SensorKeys.NetDown, "total:down", down);
        _peaks.Publish(builder, SensorKeys.NetUp, "total:up", up);
        for (var i = 0; i < interfaces.Count && i < SensorKeys.MaxDevices; i++)
        {
            var (adapter, downRate, upRate, _) = interfaces[i];
            var slot = i + 1;
            builder.SetText(SensorKeys.NetName(slot), adapter);
            // Peaks follow the device, not the slot: adapters coming and going must not inherit
            // a maximum reached by whoever held that slot before them.
            _peaks.Publish(builder, SensorKeys.NetDownAt(slot), adapter + ":down", downRate);
            _peaks.Publish(builder, SensorKeys.NetUpAt(slot), adapter + ":up", upRate);
        }

        var drives = _disk.Sample();
        for (var i = 0; i < drives.Count && i < SensorKeys.MaxDevices; i++)
        {
            var (drive, readRate, writeRate) = drives[i];
            var slot = i + 1;
            builder.SetText(SensorKeys.DiskName(slot), drive);
            _peaks.Publish(builder, SensorKeys.DiskRead(slot), drive + ":read", readRate);
            _peaks.Publish(builder, SensorKeys.DiskWrite(slot), drive + ":write", writeRate);
        }

        var (name, cpu) = _process.Top();
        builder.Set(SensorKeys.ProcTopCpu, cpu);
        builder.SetText(SensorKeys.ProcTopName, name);
    }

    public void Dispose() => _frameRate?.Dispose();

    /// <summary>
    /// Remembers the highest value each throughput sensor has reached since the monitor started, so
    /// themes can show "now / peak". Peaks are per device (identity), not per theme slot.
    /// </summary>
    public sealed class PeakTracker
    {
        private readonly Dictionary<string, double> _peaks = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Emits <paramref name="value"/> under <paramref name="key"/> and its running maximum under "<c>key</c>.peak".</summary>
        public void Publish(SensorSnapshot.Builder builder, string key, string identity, double value)
        {
            builder.Set(key, value);
            if (double.IsNaN(value)) return;

            var peak = Math.Max(_peaks.GetValueOrDefault(identity), value);
            _peaks[identity] = peak;
            builder.Set(SensorKeys.Peak(key), peak);
        }
    }


    /// <summary>Finds the process using the most CPU since the previous sample.</summary>
    private sealed class ProcessCpuMonitor
    {
        private Dictionary<int, TimeSpan> _previous = new();
        private DateTime _previousTime;

        public (string? name, double cpuPercent) Top()
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _previousTime).TotalMilliseconds;
            var current = new Dictionary<int, TimeSpan>();

            string? topName = null;
            double topCpu = 0;
            var haveBaseline = _previousTime != default;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var cpuTime = process.TotalProcessorTime;
                    current[process.Id] = cpuTime;

                    if (haveBaseline && elapsedMs > 0 && _previous.TryGetValue(process.Id, out var prev))
                    {
                        var cpuMs = (cpuTime - prev).TotalMilliseconds;
                        var pct = cpuMs / (elapsedMs * Environment.ProcessorCount) * 100.0;
                        if (pct > topCpu)
                        {
                            topCpu = pct;
                            topName = process.ProcessName;
                        }
                    }
                }
                catch { /* access denied / exited between calls */ }
                finally { process.Dispose(); }
            }

            _previous = current;
            _previousTime = now;
            return (topName, Math.Clamp(topCpu, 0, 100));
        }
    }
}
