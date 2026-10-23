namespace MyTuringSystemMonitor.Sensors;

/// <summary>
/// Cross-cutting readings that don't need LibreHardwareMonitor (disk usage). Shared by the
/// providers so every one exposes disk stats consistently.
/// </summary>
public static class SystemMetrics
{
    private const double BytesPerGb = 1_000_000_000.0; // decimal GB, matches typical drive labels

    private const double BytesPerGib = 1024.0 * 1024 * 1024;

    /// <summary>
    /// Adds physical memory (ram.*) and page-file (swap.*) usage straight from Windows. Used by every
    /// provider: it needs no admin rights, works under the service account, and — unlike
    /// LibreHardwareMonitor, which reports "Total Memory" and "Virtual Memory" as two devices with
    /// identically named sensors — can't mix the two up.
    /// </summary>
    public static void AppendMemory(SensorSnapshot.Builder builder)
    {
        var status = new MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
            return;

        var total = status.TotalPhys / BytesPerGib;
        var free = status.AvailPhys / BytesPerGib;
        var used = total - free;

        builder.Set(SensorKeys.RamTotal, total);
        builder.Set(SensorKeys.RamFree, free);
        builder.Set(SensorKeys.RamUsed, used);
        builder.Set(SensorKeys.RamLoad, used / total * 100.0);

        // The commit limit is RAM + page file; what's committed beyond physical memory is swap in use.
        var swapTotalBytes = status.TotalPageFile > status.TotalPhys ? status.TotalPageFile - status.TotalPhys : 0UL;
        if (swapTotalBytes > 0)
        {
            var committed = (double)(status.TotalPageFile - status.AvailPageFile);
            var physicalInUse = (double)(status.TotalPhys - status.AvailPhys);
            var swapUsed = Math.Clamp(committed - physicalInUse, 0, swapTotalBytes);

            builder.Set(SensorKeys.SwapTotal, swapTotalBytes / BytesPerGib);
            builder.Set(SensorKeys.SwapUsed, swapUsed / BytesPerGib);
            builder.Set(SensorKeys.SwapLoad, swapUsed / swapTotalBytes * 100.0);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

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
