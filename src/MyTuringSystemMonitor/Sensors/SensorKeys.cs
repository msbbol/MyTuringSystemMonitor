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
    public const string CpuFan = "cpu.fan";       // RPM — CPU cooler

    public const string GpuLoad = "gpu.load";       // % busiest graphics adapter, as in Task Manager (see GpuEngineMeter)
    public const string GpuTemp = "gpu.temp";       // °C
    public const string GpuPower = "gpu.power";     // W
    public const string GpuMemoryUsed = "gpu.mem";          // MB
    public const string GpuMemoryTotal = "gpu.mem.total";   // MB
    public const string GpuMemoryLoad = "gpu.mem.load";     // % of dedicated video memory in use
    public const string GpuFan = "gpu.fan";         // RPM

    // Physical memory. GB here means GiB, matching what Task Manager shows.
    public const string RamLoad = "ram.load";     // % used
    public const string RamUsed = "ram.used";     // GB
    public const string RamFree = "ram.free";     // GB available
    public const string RamTotal = "ram.total";   // GB

    // Page file (what mathoudebine themes call SWAP).
    public const string SwapLoad = "swap.load";   // % used
    public const string SwapUsed = "swap.used";   // GB
    public const string SwapTotal = "swap.total"; // GB

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

    /// <summary>How many drives / network adapters get their own numbered keys.</summary>
    public const int MaxDevices = 4;

    // Per-device throughput, numbered from 1. Drives are ordered by letter (C:, D:, …); network
    // adapters by lifetime traffic, so the one carrying the connection is net.1.
    public static string DiskName(int slot) => $"disk.{slot}.name";    // text, e.g. "C:"
    public static string DiskRead(int slot) => $"disk.{slot}.read";    // B/s
    public static string DiskWrite(int slot) => $"disk.{slot}.write";  // B/s
    public static string NetName(int slot) => $"net.{slot}.name";      // text, e.g. "Wi-Fi"
    public static string NetDownAt(int slot) => $"net.{slot}.down";    // B/s
    public static string NetUpAt(int slot) => $"net.{slot}.up";        // B/s

    /// <summary>
    /// Companion of a throughput key holding the highest value seen since the monitor started,
    /// e.g. "disk.1.read" -> "disk.1.read.peak".
    /// </summary>
    public static string Peak(string throughputKey) => throughputKey + ".peak";

    // Frame rate of the busiest rendering app (see FrameRateMeter).
    public const string Fps = "fps";                  // frames per second
    public const string FpsApp = "fps.app";           // text channel — which app it belongs to

    // Busiest process by CPU.
    public const string ProcTopCpu = "proc.top.cpu";  // %
    public const string ProcTopName = "proc.top.name"; // text channel (see SensorSnapshot.GetText)
}
