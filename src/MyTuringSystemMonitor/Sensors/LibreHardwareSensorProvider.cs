using LibreHardwareMonitor.Hardware;

namespace MyTuringSystemMonitor.Sensors;

/// <summary>One device in a <see cref="LibreHardwareSensorProvider.DumpAll"/> diagnostic listing.</summary>
public sealed record HardwareDump(string Name, string Type, IReadOnlyList<SensorDump> Sensors);

/// <summary>One sensor in a <see cref="LibreHardwareSensorProvider.DumpAll"/> diagnostic listing.</summary>
public sealed record SensorDump(string Name, string Type, float? Value);

/// <summary>
/// Reads the full sensor set (CPU/GPU/RAM load, temps, power, clocks) via LibreHardwareMonitor.
/// This is the same engine the Python project's <c>HW_SENSORS: LHM</c> mode uses and, like it,
/// needs administrator rights on Windows to load the kernel driver — without them many values
/// come back null (the app still runs; widgets bound to missing sensors just show a fallback).
/// </summary>
public sealed class LibreHardwareSensorProvider : ISensorProvider
{
    private readonly Computer _computer;
    private readonly SystemDynamics _dynamics;
    private bool _opened;

    /// <param name="frameRate">Measure FPS from ETW present events.</param>
    public LibreHardwareSensorProvider(bool frameRate = true)
    {
        _dynamics = new SystemDynamics(frameRate);
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = false,   // read via SystemMetrics.AppendMemory instead
            // Fan speeds are why these two are on: desktops put them on the motherboard's SuperIO
            // chip, laptops behind the embedded controller, and LHM groups those differently.
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

        b.Set(SensorKeys.CpuFan, CpuFanRpm());

        // Memory comes from Windows directly: LHM exposes "Total Memory" and "Virtual Memory" as two
        // devices with identically named sensors, so whichever it listed last used to win.
        SystemMetrics.AppendMemory(b);
        SystemMetrics.AppendDisk(b);
        SystemMetrics.AppendTime(b);
        _dynamics.Append(b);

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
        b.SetText(SensorKeys.CpuName, string.IsNullOrWhiteSpace(hw.Name) ? SystemMetrics.CpuName() : hw.Name);
        b.Set(SensorKeys.CpuLoad, Pick(hw, SensorType.Load, "CPU Total"));
        b.Set(SensorKeys.CpuTemp, Pick(hw, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max"));
        b.Set(SensorKeys.CpuPower, Pick(hw, SensorType.Power, "CPU Package", "Package"));
        b.Set(SensorKeys.CpuClock, PickMax(hw, SensorType.Clock, "Core"));
    }

    /// <summary>
    /// The CPU cooler's speed. Unlike load or temperature it doesn't hang off the CPU node: fans
    /// are wired to the motherboard, so they surface under its SuperIO/EC sub-hardware — and on
    /// many laptops the embedded controller exposes nothing at all, which reads as "no fan".
    /// </summary>
    private double? CpuFanRpm()
    {
        // Everything except the graphics cards, which report their own fans separately: the CPU
        // cooler can surface under the motherboard, its SuperIO sub-chip or — on laptops — the
        // embedded controller, and which one it is differs per machine.
        var fans = Fans(_computer.Hardware.Where(h => !IsGpu(h.HardwareType)));
        return BestFan(fans, "cpu");
    }

    private static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    /// <summary>
    /// Everything this provider's own LibreHardwareMonitor instance can see, sensor by sensor.
    /// A diagnostic for questions the theme can't answer — "does this laptop report fan speeds at
    /// all, and under which device?".
    /// </summary>
    public IReadOnlyList<HardwareDump> Dump()
    {
        if (!_opened) Initialize();

        var dump = new List<HardwareDump>();
        foreach (var hw in _computer.Hardware)
        {
            Refresh(hw);
            Collect(hw, dump);
        }
        return dump;
    }

    /// <summary>
    /// The same listing with every hardware group turned on, for when no LHM provider is running.
    /// Never call this while one is: LibreHardwareMonitor shares a single kernel driver between
    /// instances, and closing this one would pull it out from under the live provider.
    /// </summary>
    public static IReadOnlyList<HardwareDump> DumpAll()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsBatteryEnabled = true,
            IsPsuEnabled = true,
        };

        var dump = new List<HardwareDump>();
        try
        {
            computer.Open();
            foreach (var hw in computer.Hardware)
            {
                Refresh(hw);
                Collect(hw, dump);
            }
        }
        finally
        {
            try { computer.Close(); } catch { /* driver may refuse to unload */ }
        }

        return dump;
    }

    private static void Collect(IHardware hw, List<HardwareDump> into)
    {
        into.Add(new HardwareDump(
            hw.Name,
            hw.HardwareType.ToString(),
            hw.Sensors
                .Select(s => new SensorDump(s.Name, s.SensorType.ToString(), s.Value))
                .OrderBy(s => s.Type, StringComparer.Ordinal)
                .ToArray()));

        foreach (var sub in hw.SubHardware)
            Collect(sub, into);
    }

    private static void ReadGpu(IHardware hw, SensorSnapshot.Builder b)
    {
        b.Set(SensorKeys.GpuLoad, Pick(hw, SensorType.Load, "GPU Core", "D3D 3D"));
        b.Set(SensorKeys.GpuTemp, Pick(hw, SensorType.Temperature, "GPU Core", "GPU Hot Spot"));
        b.Set(SensorKeys.GpuPower, Pick(hw, SensorType.Power, "GPU Package", "GPU Power", "GPU PPT"));
        var memUsed = Pick(hw, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        var memTotal = Pick(hw, SensorType.SmallData, "GPU Memory Total");
        b.Set(SensorKeys.GpuMemoryUsed, memUsed);
        b.Set(SensorKeys.GpuMemoryTotal, memTotal);
        b.Set(SensorKeys.GpuMemoryLoad, memUsed is { } used && memTotal is > 0
            ? used / memTotal.Value * 100.0
            : Pick(hw, SensorType.Load, "GPU Memory"));
        // A graphics card carries its own fans, so these are on the GPU node itself.
        b.Set(SensorKeys.GpuFan, BestFan(Fans([hw]), "gpu"));
    }

    /// <summary>Every fan sensor in a hardware tree, as (name, RPM) pairs.</summary>
    private static IEnumerable<(string Name, double Rpm)> Fans(IEnumerable<IHardware> tree)
    {
        foreach (var hw in tree)
        {
            foreach (var sensor in hw.Sensors)
                if (sensor.SensorType == SensorType.Fan && sensor.Value is float rpm)
                    yield return (sensor.Name, rpm);

            foreach (var pair in Fans(hw.SubHardware))
                yield return pair;
        }
    }

    /// <summary>
    /// Picks the fan to report: one whose name mentions <paramref name="preferred"/> ("CPU Fan",
    /// "GPU Fan"), otherwise the fastest one spinning. Boards list a sensor per header, so the ones
    /// reading 0 RPM are empty connectors rather than a stopped fan. Pure, so it can be unit-tested.
    /// </summary>
    public static double? BestFan(IEnumerable<(string Name, double Rpm)> fans, string preferred)
    {
        double? best = null;
        foreach (var (name, rpm) in fans)
        {
            if (rpm <= 0) continue;
            if (name.Contains(preferred, StringComparison.OrdinalIgnoreCase)) return rpm;
            if (best is null || rpm > best) best = rpm;
        }
        return best;
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
        _dynamics.Dispose();
        if (!_opened) return;
        try { _computer.Close(); } catch { /* driver may already be gone */ }
        _opened = false;
    }
}
