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

    public const string GpuLoad = "gpu.load";     // %
    public const string GpuTemp = "gpu.temp";     // °C
    public const string GpuPower = "gpu.power";   // W

    public const string RamLoad = "ram.load";     // % used
    public const string RamUsed = "ram.used";     // GB
    public const string RamTotal = "ram.total";   // GB
}
