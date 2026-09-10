using System.Diagnostics;
using System.Net.NetworkInformation;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Stateful, rate-based readings that need a previous sample: network throughput and the busiest
/// process by CPU. One instance per provider; call <see cref="Append"/> once per refresh.
/// </summary>
public sealed class SystemDynamics
{
    private readonly NetworkMeter _network = new();
    private readonly ProcessCpuMonitor _process = new();

    public void Append(SensorSnapshot.Builder builder)
    {
        var (down, up) = _network.Sample();
        builder.Set(SensorKeys.NetDown, down);
        builder.Set(SensorKeys.NetUp, up);

        var (name, cpu) = _process.Top();
        builder.Set(SensorKeys.ProcTopCpu, cpu);
        builder.SetText(SensorKeys.ProcTopName, name);
    }

    /// <summary>Sums received/sent bytes across active interfaces and reports the per-second rate.</summary>
    private sealed class NetworkMeter
    {
        private long _prevRx;
        private long _prevTx;
        private DateTime _prevTime;
        private bool _initialised;

        public (double downBytesPerSec, double upBytesPerSec) Sample()
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                try
                {
                    var stats = ni.GetIPv4Statistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
                catch { /* some interfaces don't expose stats */ }
            }

            var now = DateTime.UtcNow;
            double down = 0, up = 0;
            if (_initialised)
            {
                var seconds = (now - _prevTime).TotalSeconds;
                if (seconds > 0)
                {
                    down = Math.Max(0, (rx - _prevRx) / seconds);
                    up = Math.Max(0, (tx - _prevTx) / seconds);
                }
            }

            _prevRx = rx;
            _prevTx = tx;
            _prevTime = now;
            _initialised = true;
            return (down, up);
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
