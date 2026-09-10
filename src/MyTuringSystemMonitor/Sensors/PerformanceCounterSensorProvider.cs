using System.Diagnostics;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Lightweight sensor source using Windows performance counters. Covers CPU load and RAM
/// commit % without administrator rights or a kernel driver — a safe default/fallback when
/// LibreHardwareMonitor can't run. Temps, power and clocks are not available here (left null).
/// </summary>
public sealed class PerformanceCounterSensorProvider : ISensorProvider
{
    private readonly SystemDynamics _dynamics = new();
    private PerformanceCounter? _cpu;
    private PerformanceCounter? _ramCommitted;

    public void Initialize()
    {
        _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        try { _ramCommitted = new PerformanceCounter("Memory", "% Committed Bytes In Use"); }
        catch { _ramCommitted = null; }

        // The first sample of a rate counter is always 0 — prime them so the first Read() is real.
        _cpu.NextValue();
        _ramCommitted?.NextValue();
    }

    public SensorSnapshot Read()
    {
        if (_cpu is null) Initialize();

        var b = new SensorSnapshot.Builder();
        b.SetText(SensorKeys.CpuName, SystemMetrics.CpuName());
        b.Set(SensorKeys.CpuLoad, _cpu!.NextValue());
        if (_ramCommitted is not null)
            b.Set(SensorKeys.RamLoad, _ramCommitted.NextValue());
        SystemMetrics.AppendDisk(b);
        SystemMetrics.AppendTime(b);
        _dynamics.Append(b);
        return b.Build();
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _ramCommitted?.Dispose();
    }
}
