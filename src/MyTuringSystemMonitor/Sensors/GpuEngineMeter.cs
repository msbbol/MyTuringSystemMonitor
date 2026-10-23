using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// GPU utilisation the way Task Manager computes it, from the Windows "GPU Engine" performance
/// counters, across every graphics adapter.
/// <para>
/// Why not LibreHardwareMonitor's "GPU Core" load: on laptops with switchable graphics (Optimus)
/// the desktop and most apps run on the integrated GPU, which LHM often doesn't detect at all,
/// while the discrete GPU sleeps and reads 0 % almost all the time. Taking the busiest adapter
/// shows real activity — the integrated GPU day to day, the discrete one in games.
/// </para>
/// Needs no admin rights and works under the service account.
/// </summary>
public sealed class GpuEngineMeter
{
    // e.g. "pid_1234_luid_0x00000000_0x00016463_phys_0_eng_0_engtype_3D"
    private static readonly Regex InstancePattern = new(
        @"luid_(?<luid>0x[0-9a-f]+_0x[0-9a-f]+)_phys_\d+_eng_(?<engine>\d+)_",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private PerformanceCounterCategory? _category;
    private Dictionary<string, CounterSample>? _previous;
    private bool _unavailable;

    /// <summary>Busiest adapter's utilisation in %, or null (first call, or counters unavailable).</summary>
    public double? Sample()
    {
        if (_unavailable) return null;

        Dictionary<string, CounterSample> current;
        try
        {
            _category ??= new PerformanceCounterCategory("GPU Engine");
            var data = _category.ReadCategory()["Utilization Percentage"];
            if (data is null) return null;

            current = new Dictionary<string, CounterSample>(data.Count, StringComparer.Ordinal);
            foreach (InstanceData instance in data.Values)
                current[instance.InstanceName] = instance.Sample;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Older Windows (no WDDM 2.x GPU counters) or counters disabled: don't retry every frame.
            _unavailable = true;
            return null;
        }

        var previous = _previous;
        _previous = current;
        if (previous is null) return null;   // utilisation is a rate: it needs two samples

        var readings = new List<(string Instance, double Percent)>(current.Count);
        foreach (var (name, sample) in current)
        {
            // Processes come and go between samples; only instances present both times can be rated.
            if (previous.TryGetValue(name, out var before))
                readings.Add((name, CounterSample.Calculate(before, sample)));
        }

        return BusiestAdapter(readings);
    }

    /// <summary>
    /// Task Manager's rule: an engine's load is the sum over processes using it, an adapter's load is
    /// its busiest engine, and we report the busiest adapter. Pure, so it can be unit-tested.
    /// </summary>
    public static double? BusiestAdapter(IEnumerable<(string Instance, double Percent)> readings)
    {
        var perEngine = new Dictionary<(string Luid, string Engine), double>();
        foreach (var (instance, percent) in readings)
        {
            var match = InstancePattern.Match(instance);
            if (!match.Success || double.IsNaN(percent)) continue;
            var key = (match.Groups["luid"].Value.ToLowerInvariant(), match.Groups["engine"].Value);
            perEngine[key] = perEngine.GetValueOrDefault(key) + Math.Max(0, percent);
        }

        return perEngine.Count == 0 ? null : Math.Min(100, perEngine.Values.Max());
    }
}
