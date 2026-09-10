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

        var builder = new SensorSnapshot.Builder()
            .Set(SensorKeys.CpuLoad, Wave(0))
            .Set(SensorKeys.CpuTemp, 40 + Wave(1) * 0.4)
            .Set(SensorKeys.CpuClock, 3000 + Wave(0) * 15)
            .Set(SensorKeys.CpuPower, 20 + Wave(2) * 0.6)
            .Set(SensorKeys.GpuLoad, Wave(3))
            .Set(SensorKeys.GpuTemp, 45 + Wave(4) * 0.4)
            .Set(SensorKeys.GpuPower, 30 + Wave(5) * 1.2)
            .Set(SensorKeys.GpuMemoryUsed, 4096 + Wave(3) * 40)
            .Set(SensorKeys.RamLoad, Wave(6))
            .Set(SensorKeys.RamTotal, 32)
            .Set(SensorKeys.RamUsed, 32 * Wave(6) / 100.0)
            .Set(SensorKeys.DiskTotal, 1000)
            .Set(SensorKeys.DiskUsed, 500)
            .Set(SensorKeys.DiskFree, 500)
            .Set(SensorKeys.DiskLoad, 50)
            .Set(SensorKeys.NetDown, 7_400_000 + Wave(1) * 40_000)
            .Set(SensorKeys.NetUp, 860_000 + Wave(2) * 8_000)
            .Set(SensorKeys.ProcTopCpu, 12.6)
            .SetText(SensorKeys.ProcTopName, "firefox")
            .SetText(SensorKeys.CpuName, "12th Gen Intel(R) Core(TM) i7-12700H");

        // Real wall-clock time so clock themes preview correctly.
        SystemMetrics.AppendTime(builder);
        return builder.Build();
    }

    public void Dispose() { }
}
