namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Canonical string keys for sensor values. Themes (see <c>themes/default.json</c>) bind
/// widgets to sensors by these strings, so the rendering layer never depends on
/// LibreHardwareMonitor types directly. Keep these stable — changing one breaks existing themes.
/// </summary>
public static class SensorKeys
{
    public const string CpuLoad = "cpu.load";     // %
    public const string CpuTemp = "cpu.temp";     // °C
    public const string CpuPower = "cpu.power";   // W
    public const string CpuClock = "cpu.clock";   // MHz
    public const string CpuName = "cpu.name";     // text channel — CPU model string

    public const string GpuLoad = "gpu.load";       // %
    public const string GpuTemp = "gpu.temp";       // °C
    public const string GpuPower = "gpu.power";     // W
    public const string GpuMemoryUsed = "gpu.mem";  // MB

    public const string RamLoad = "ram.load";     // % used
    public const string RamUsed = "ram.used";     // GB
    public const string RamTotal = "ram.total";   // GB

    public const string DiskLoad = "disk.load";   // % used
    public const string DiskUsed = "disk.used";   // GB
    public const string DiskFree = "disk.free";   // GB
    public const string DiskTotal = "disk.total"; // GB

    // Clock/uptime pseudo-sensors (so gauges/clocks can bind to time).
    public const string TimeSecond = "time.second";   // 0..60 (with fraction)
    public const string TimeMinute = "time.minute";   // 0..60 (with fraction)
    public const string TimeHour12 = "time.hour12";   // 0..12 (with fraction)
    public const string UptimeHours = "sys.uptime";   // hours since boot

    // Network throughput (bytes per second, summed over active interfaces).
    public const string NetDown = "net.down";         // B/s received
    public const string NetUp = "net.up";             // B/s sent

    // Busiest process by CPU.
    public const string ProcTopCpu = "proc.top.cpu";  // %
    public const string ProcTopName = "proc.top.name"; // text channel (see SensorSnapshot.GetText)
}
