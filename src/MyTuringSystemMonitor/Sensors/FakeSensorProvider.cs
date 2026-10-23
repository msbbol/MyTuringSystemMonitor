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
            .Set(SensorKeys.CpuFan, 2200 + Wave(0) * 12)
            .Set(SensorKeys.GpuFan, 1800 + Wave(3) * 10)
            .Set(SensorKeys.GpuLoad, Wave(3))
            .Set(SensorKeys.GpuTemp, 45 + Wave(4) * 0.4)
            .Set(SensorKeys.GpuPower, 30 + Wave(5) * 1.2)
            .Set(SensorKeys.GpuMemoryUsed, 4096 + Wave(3) * 40)
            .Set(SensorKeys.GpuMemoryTotal, 8192)
            .Set(SensorKeys.GpuMemoryLoad, (4096 + Wave(3) * 40) / 8192 * 100)
            .Set(SensorKeys.RamLoad, Wave(6))
            .Set(SensorKeys.RamTotal, 32)
            .Set(SensorKeys.RamUsed, 32 * Wave(6) / 100.0)
            .Set(SensorKeys.RamFree, 32 - 32 * Wave(6) / 100.0)
            .Set(SensorKeys.SwapTotal, 8)
            .Set(SensorKeys.SwapUsed, 8 * Wave(7) / 100.0)
            .Set(SensorKeys.SwapLoad, Wave(7))
            .Set(SensorKeys.DiskTotal, 1000)
            .Set(SensorKeys.DiskUsed, 500)
            .Set(SensorKeys.DiskFree, 500)
            .Set(SensorKeys.DiskLoad, 50)
            .Set(SensorKeys.NetDown, 7_400_000 + Wave(1) * 40_000)
            .Set(SensorKeys.NetUp, 860_000 + Wave(2) * 8_000)
            .SetText(SensorKeys.NetName(1), "Wi-Fi")
            .Set(SensorKeys.NetDownAt(1), 7_400_000 + Wave(1) * 40_000)
            .Set(SensorKeys.NetUpAt(1), 860_000 + Wave(2) * 8_000)
            .SetText(SensorKeys.NetName(2), "Ethernet")
            .Set(SensorKeys.NetDownAt(2), 120_000 + Wave(4) * 900)
            .Set(SensorKeys.NetUpAt(2), 40_000 + Wave(5) * 300)
            .SetText(SensorKeys.DiskName(1), "C:")
            .Set(SensorKeys.DiskRead(1), 32_000_000 + Wave(2) * 200_000)
            .Set(SensorKeys.DiskWrite(1), 8_000_000 + Wave(3) * 90_000)
            .SetText(SensorKeys.DiskName(2), "D:")
            .Set(SensorKeys.DiskRead(2), 2_000_000 + Wave(6) * 20_000)
            .Set(SensorKeys.DiskWrite(2), 500_000 + Wave(7) * 6_000)
            .Set(SensorKeys.Peak(SensorKeys.NetDownAt(1)), 24_000_000)
            .Set(SensorKeys.Peak(SensorKeys.NetUpAt(1)), 3_100_000)
            .Set(SensorKeys.Peak(SensorKeys.NetDownAt(2)), 900_000)
            .Set(SensorKeys.Peak(SensorKeys.NetUpAt(2)), 210_000)
            .Set(SensorKeys.Peak(SensorKeys.DiskRead(1)), 1_240_000_000)
            .Set(SensorKeys.Peak(SensorKeys.DiskWrite(1)), 680_000_000)
            .Set(SensorKeys.Peak(SensorKeys.DiskRead(2)), 45_000_000)
            .Set(SensorKeys.Peak(SensorKeys.DiskWrite(2)), 12_000_000)
            .Set(SensorKeys.Fps, 60 + Wave(8) * 0.9)
            .SetText(SensorKeys.FpsApp, "demo-game")
            .Set(SensorKeys.ProcTopCpu, 12.6)
            .SetText(SensorKeys.ProcTopName, "firefox")
            .SetText(SensorKeys.CpuName, "12th Gen Intel(R) Core(TM) i7-12700H");

        // Real wall-clock time so clock themes preview correctly.
        SystemMetrics.AppendTime(builder);
        return builder.Build();
    }

    public void Dispose() { }
}
