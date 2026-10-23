using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>Read/write throughput of one drive, in bytes per second.</summary>
public sealed record DriveThroughput(string Name, double ReadBytesPerSecond, double WriteBytesPerSecond);

/// <summary>
/// Per-drive disk throughput from the Windows "LogicalDisk" counters — one entry per drive letter,
/// ordered C:, D:, E: … Volumes without a letter (recovery, EFI) are skipped.
/// </summary>
public sealed class DiskIoMeter
{
    private const string ReadCounter = "Disk Read Bytes/sec";
    private const string WriteCounter = "Disk Write Bytes/sec";

    private static readonly Regex DriveLetter = new(@"^[A-Za-z]:$", RegexOptions.Compiled);

    private PerformanceCounterCategory? _category;
    private Dictionary<string, (CounterSample Read, CounterSample Write)>? _previous;
    private bool _unavailable;

    /// <summary>Drives with their current throughput; empty on the first call or if unavailable.</summary>
    public IReadOnlyList<DriveThroughput> Sample()
    {
        if (_unavailable) return [];

        Dictionary<string, (CounterSample Read, CounterSample Write)> current;
        try
        {
            _category ??= new PerformanceCounterCategory("LogicalDisk");
            var data = _category.ReadCategory();
            var reads = data[ReadCounter];
            var writes = data[WriteCounter];
            if (reads is null || writes is null) return [];

            current = new Dictionary<string, (CounterSample, CounterSample)>(StringComparer.OrdinalIgnoreCase);
            foreach (InstanceData instance in reads.Values)
            {
                if (!IsDrive(instance.InstanceName)) continue;   // skips _Total and HarddiskVolumeN
                var write = writes[instance.InstanceName];
                if (write is null) continue;
                current[instance.InstanceName] = (instance.Sample, write.Sample);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _unavailable = true;
            return [];
        }

        var previous = _previous;
        _previous = current;
        if (previous is null) return [];   // throughput is a rate: it needs two samples

        var result = new List<DriveThroughput>(current.Count);
        foreach (var (name, now) in current)
        {
            if (!previous.TryGetValue(name, out var before)) continue;
            result.Add(new DriveThroughput(
                name,
                Math.Max(0, CounterSample.Calculate(before.Read, now.Read)),
                Math.Max(0, CounterSample.Calculate(before.Write, now.Write))));
        }

        return InLetterOrder(result);
    }

    /// <summary>
    /// True for a real drive ("C:") and false for the aggregate "_Total" and for letterless volumes
    /// ("HarddiskVolume3"). Pure, so it can be unit-tested.
    /// </summary>
    public static bool IsDrive(string instanceName) => DriveLetter.IsMatch(instanceName);

    /// <summary>
    /// Drives in the order themes bind them to disk.1, disk.2 …: C:, D:, E:. Pure, so it can be
    /// unit-tested.
    /// </summary>
    public static IReadOnlyList<DriveThroughput> InLetterOrder(IEnumerable<DriveThroughput> drives)
    {
        var ordered = drives.ToList();
        ordered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return ordered;
    }
}
