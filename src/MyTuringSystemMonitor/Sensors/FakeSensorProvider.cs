namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// A no-hardware sensor source. Produces smoothly varying synthetic values so the rendering
/// loop can be exercised without admin rights or a real machine — used by tests and by
/// <c>sensorProvider: "Fake"</c> in config for a dry run.
/// </summary>
public sealed class FakeSensorProvider : ISensorProvider
{
    private readonly Func<double> _clock;
    private int _tick;

    /// <param name="clock">
    /// Returns a monotonically increasing time-ish value (seconds). Defaults to a per-Read
    /// counter so output is deterministic in tests.
    /// </param>
    public FakeSensorProvider(Func<double>? clock = null)
    {
        _clock = clock ?? (() => _tick);
    }

    public void Initialize() { }

    public SensorSnapshot Read()
    {
        _tick++;
        var t = _clock();

        // Deterministic-ish waves in [0,100] so gauges and text have something to show.
        double Wave(double phase) => 50 + 45 * Math.Sin((t + phase) * 0.5);

        return new SensorSnapshot.Builder()
            .Set(SensorKeys.CpuLoad, Wave(0))
            .Set(SensorKeys.CpuTemp, 40 + Wave(1) * 0.4)
            .Set(SensorKeys.CpuClock, 3000 + Wave(0) * 15)
            .Set(SensorKeys.CpuPower, 20 + Wave(2) * 0.6)
            .Set(SensorKeys.GpuLoad, Wave(3))
            .Set(SensorKeys.GpuTemp, 45 + Wave(4) * 0.4)
            .Set(SensorKeys.GpuPower, 30 + Wave(5) * 1.2)
            .Set(SensorKeys.RamLoad, Wave(6))
            .Set(SensorKeys.RamTotal, 32)
            .Set(SensorKeys.RamUsed, 32 * Wave(6) / 100.0)
            .Build();
    }

    public void Dispose() { }
}
