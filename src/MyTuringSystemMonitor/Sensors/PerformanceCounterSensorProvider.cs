using System.Diagnostics;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Lightweight sensor source using Windows performance counters. Covers CPU load plus memory, disk
/// and network without administrator rights or a kernel driver — a safe default/fallback when
/// LibreHardwareMonitor can't run. Temps, power and clocks are not available here (left null).
/// </summary>
public sealed class PerformanceCounterSensorProvider : ISensorProvider
{
    private readonly SystemDynamics _dynamics;
    private PerformanceCounter? _cpu;

    /// <param name="frameRate">Measure FPS from ETW present events.</param>
    public PerformanceCounterSensorProvider(bool frameRate = true)
    {
        _dynamics = new SystemDynamics(frameRate);
    }

    public void Initialize()
    {
        if (_cpu is not null) return;   // may be called again when the monitor restarts

        _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");

        // The first sample of a rate counter is always 0 — prime it so the first Read() is real.
        _cpu.NextValue();
    }

    public SensorSnapshot Read()
    {
        if (_cpu is null) Initialize();

        var b = new SensorSnapshot.Builder();
        b.SetText(SensorKeys.CpuName, SystemMetrics.CpuName());
        b.Set(SensorKeys.CpuLoad, _cpu!.NextValue());
        SystemMetrics.AppendMemory(b);
        SystemMetrics.AppendDisk(b);
        SystemMetrics.AppendTime(b);
        _dynamics.Append(b);
        return b.Build();
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _dynamics.Dispose();
    }
}
