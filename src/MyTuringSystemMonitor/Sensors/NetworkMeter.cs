using System.Net.NetworkInformation;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>Download/upload throughput of one network adapter, in bytes per second.</summary>
public sealed record InterfaceThroughput(string Name, double DownBytesPerSecond, double UpBytesPerSecond, long TotalBytes);

/// <summary>
/// Network throughput per adapter and in total, from the adapters' byte counters. Loopback and
/// tunnel adapters are skipped; the rest are ordered by lifetime traffic, so the adapter actually
/// carrying the connection (Wi-Fi, Ethernet) comes first and virtual ones (WSL, Hyper-V) after it.
/// </summary>
public sealed class NetworkMeter
{
    private readonly Dictionary<string, (long Rx, long Tx)> _previous = new(StringComparer.Ordinal);
    private DateTime _previousTime;
    private bool _initialised;

    public (double DownBytesPerSecond, double UpBytesPerSecond, IReadOnlyList<InterfaceThroughput> Interfaces) Sample()
    {
        var current = new Dictionary<string, (long Rx, long Tx)>(StringComparer.Ordinal);
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsUsable(adapter.OperationalStatus, adapter.NetworkInterfaceType)) continue;
            try
            {
                var stats = adapter.GetIPv4Statistics();
                current[adapter.Name] = (stats.BytesReceived, stats.BytesSent);
            }
            catch { /* some adapters don't expose statistics */ }
        }

        var now = DateTime.UtcNow;
        var seconds = (now - _previousTime).TotalSeconds;
        var rates = new List<InterfaceThroughput>(current.Count);
        double totalDown = 0, totalUp = 0;

        foreach (var (name, counters) in current)
        {
            double down = 0, up = 0;
            if (_initialised && seconds > 0 && _previous.TryGetValue(name, out var before))
            {
                down = Math.Max(0, (counters.Rx - before.Rx) / seconds);
                up = Math.Max(0, (counters.Tx - before.Tx) / seconds);
            }

            totalDown += down;
            totalUp += up;
            rates.Add(new InterfaceThroughput(name, down, up, counters.Rx + counters.Tx));
        }

        _previous.Clear();
        foreach (var (name, counters) in current) _previous[name] = counters;
        _previousTime = now;
        _initialised = true;

        return (totalDown, totalUp, ByTraffic(rates));
    }

    /// <summary>
    /// An adapter worth reporting: connected, and neither loopback nor a tunnel (VPN pseudo-adapters
    /// would double-count the traffic of the adapter underneath). Pure, so it can be unit-tested.
    /// </summary>
    public static bool IsUsable(OperationalStatus status, NetworkInterfaceType type) =>
        status == OperationalStatus.Up &&
        type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel);

    /// <summary>
    /// Adapters in the order themes bind them to net.1, net.2 …: most lifetime traffic first, so the
    /// one actually carrying the connection wins over idle virtual switches. Pure, so it can be
    /// unit-tested.
    /// </summary>
    public static IReadOnlyList<InterfaceThroughput> ByTraffic(IEnumerable<InterfaceThroughput> adapters)
    {
        var ordered = adapters.ToList();
        ordered.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
        return ordered;
    }
}
