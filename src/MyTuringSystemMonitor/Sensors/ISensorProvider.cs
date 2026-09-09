namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Supplies hardware readings as a <see cref="SensorSnapshot"/>. Implementations hide the
/// underlying source (LibreHardwareMonitor, performance counters, or a fake for tests) so the
/// rest of the app depends only on plain values.
/// </summary>
public interface ISensorProvider : IDisposable
{
    /// <summary>Optional one-time setup (open hardware, warm up counters). Safe to call once.</summary>
    void Initialize();

    /// <summary>Reads the current values. Called on every refresh tick.</summary>
    SensorSnapshot Read();
}
