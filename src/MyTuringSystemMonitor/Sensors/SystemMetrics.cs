namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Cross-cutting readings that don't need LibreHardwareMonitor (disk usage). Shared by the
/// providers so every one exposes disk stats consistently.
/// </summary>
public static class SystemMetrics
{
    private const double BytesPerGb = 1_000_000_000.0; // decimal GB, matches typical drive labels

    private static string? _cpuName;

    /// <summary>The CPU model string from the registry (cached), or null if unavailable.</summary>
    public static string? CpuName()
    {
        if (_cpuName is not null) return _cpuName;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            _cpuName = (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch { _cpuName = null; }
        return _cpuName;
    }

    /// <summary>Adds clock-derived values (time.second/minute/hour12) and uptime for clock themes.</summary>
    public static void AppendTime(SensorSnapshot.Builder builder)
    {
        var now = DateTime.Now;
        builder.Set(SensorKeys.TimeSecond, now.Second + now.Millisecond / 1000.0);
        builder.Set(SensorKeys.TimeMinute, now.Minute + now.Second / 60.0);
        builder.Set(SensorKeys.TimeHour12, (now.Hour % 12) + now.Minute / 60.0);
        builder.Set(SensorKeys.UptimeHours, Environment.TickCount64 / 3_600_000.0);
    }

    /// <summary>Adds disk.total/used/free (GB) and disk.load (%) for the OS drive.</summary>
    public static void AppendDisk(SensorSnapshot.Builder builder)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
            if (string.IsNullOrEmpty(root)) return;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;

            var total = drive.TotalSize / BytesPerGb;
            var free = drive.TotalFreeSpace / BytesPerGb;
            var used = total - free;

            builder.Set(SensorKeys.DiskTotal, total);
            builder.Set(SensorKeys.DiskFree, free);
            builder.Set(SensorKeys.DiskUsed, used);
            builder.Set(SensorKeys.DiskLoad, total > 0 ? used / total * 100.0 : null);
        }
        catch
        {
            // leave disk keys unset if the drive can't be queried
        }
    }
}
