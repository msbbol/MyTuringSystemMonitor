using LibreHardwareMonitor.Hardware;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Reads the full sensor set (CPU/GPU/RAM load, temps, power, clocks) via LibreHardwareMonitor.
/// This is the same engine the Python project's <c>HW_SENSORS: LHM</c> mode uses and, like it,
/// needs administrator rights on Windows to load the kernel driver — without them many values
/// come back null (the app still runs; widgets bound to missing sensors just show a fallback).
/// </summary>
public sealed class LibreHardwareSensorProvider : ISensorProvider
{
    private readonly Computer _computer;
    private bool _opened;

    public LibreHardwareSensorProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
        };
    }

    public void Initialize()
    {
        if (_opened) return;
        _computer.Open();
        _opened = true;
    }

    public SensorSnapshot Read()
    {
        if (!_opened) Initialize();

        foreach (var hw in _computer.Hardware)
            Refresh(hw);

        var b = new SensorSnapshot.Builder();

        foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
            ReadCpu(hw, b);

        var gpu = PrimaryGpu();
        if (gpu is not null)
            ReadGpu(gpu, b);

        foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Memory))
            ReadMemory(hw, b);

        return b.Build();
    }

    private IHardware? PrimaryGpu()
    {
        var gpus = _computer.Hardware
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToArray();

        // Prefer a GPU that actually reports a core load (skips idle integrated GPUs).
        return gpus.FirstOrDefault(g => Pick(g, SensorType.Load, "GPU Core") is not null)
               ?? gpus.FirstOrDefault();
    }

    private static void ReadCpu(IHardware hw, SensorSnapshot.Builder b)
    {
        b.Set(SensorKeys.CpuLoad, Pick(hw, SensorType.Load, "CPU Total"));
        b.Set(SensorKeys.CpuTemp, Pick(hw, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max"));
        b.Set(SensorKeys.CpuPower, Pick(hw, SensorType.Power, "CPU Package", "Package"));
        b.Set(SensorKeys.CpuClock, PickMax(hw, SensorType.Clock, "Core"));
    }

    private static void ReadGpu(IHardware hw, SensorSnapshot.Builder b)
    {
        b.Set(SensorKeys.GpuLoad, Pick(hw, SensorType.Load, "GPU Core", "D3D 3D"));
        b.Set(SensorKeys.GpuTemp, Pick(hw, SensorType.Temperature, "GPU Core", "GPU Hot Spot"));
        b.Set(SensorKeys.GpuPower, Pick(hw, SensorType.Power, "GPU Package", "GPU Power", "GPU PPT"));
    }

    private static void ReadMemory(IHardware hw, SensorSnapshot.Builder b)
    {
        b.Set(SensorKeys.RamLoad, Pick(hw, SensorType.Load, "Memory"));
        var used = Pick(hw, SensorType.Data, "Memory Used");
        var available = Pick(hw, SensorType.Data, "Memory Available");
        b.Set(SensorKeys.RamUsed, used);
        if (used.HasValue && available.HasValue)
            b.Set(SensorKeys.RamTotal, used + available);
    }

    private static void Refresh(IHardware hw)
    {
        hw.Update();
        foreach (var sub in hw.SubHardware)
            Refresh(sub);
    }

    /// <summary>First sensor of <paramref name="type"/> matching a preferred name, else first available.</summary>
    private static double? Pick(IHardware hw, SensorType type, params string[] preferredNames)
    {
        var matches = hw.Sensors.Where(s => s.SensorType == type).ToArray();
        foreach (var name in preferredNames)
        {
            var m = matches.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (m?.Value is float v) return v;
        }
        return matches.FirstOrDefault(s => s.Value.HasValue)?.Value is float fv ? fv : null;
    }

    /// <summary>Highest value among sensors of <paramref name="type"/> whose name contains <paramref name="nameContains"/>.</summary>
    private static double? PickMax(IHardware hw, SensorType type, string nameContains)
    {
        var values = hw.Sensors
            .Where(s => s.SensorType == type && s.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
            .Select(s => (double)s.Value!.Value)
            .ToArray();
        if (values.Length > 0) return values.Max();
        return Pick(hw, type);
    }

    public void Dispose()
    {
        if (!_opened) return;
        try { _computer.Close(); } catch { /* driver may already be gone */ }
        _opened = false;
    }
}
