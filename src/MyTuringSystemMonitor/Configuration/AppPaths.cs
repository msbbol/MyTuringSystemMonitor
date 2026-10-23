namespace MyTuringSystemMonitor.Configuration;

/// <summary>
/// Shared file locations. Config and themes live in a machine-wide folder under
/// <c>%ProgramData%</c> so the Windows service (running as LocalSystem), the tray app and the
/// tools all read and write the same files. Fonts are static and stay next to the executable.
/// First run seeds the data folder from the files bundled with the app.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "MyTuringSystemMonitor";

    /// <summary>Set to use a different data folder (dev runs, tests) instead of %ProgramData%.</summary>
    public const string DataDirOverrideVariable = "TURING_MONITOR_DATA";

    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable(DataDirOverrideVariable) is { Length: > 0 } overridden
            ? Path.GetFullPath(overridden)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    /// <summary>Where settings lived before the service existed (per-user LocalAppData).</summary>
    public static string LegacyUserDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string ConfigPath => Path.Combine(DataDir, "config.json");
    public static string ThemesDir => Path.Combine(DataDir, "themes");

    /// <summary>Font files ship next to the exe (read-only, not user-edited).</summary>
    public static string FontsDir => Path.Combine(AppContext.BaseDirectory, "fonts");

    /// <summary>
    /// Ensures the data folder exists, carries over settings from the old per-user location,
    /// seeds config.json on first run, and merges any bundled themes that aren't present yet from
    /// <paramref name="seedBaseDir"/> (typically the exe dir). Existing/edited themes are left
    /// untouched, so new bundled themes appear after an upgrade without clobbering the user's own.
    /// </summary>
    public static void EnsureSeeded(string seedBaseDir)
    {
        Directory.CreateDirectory(DataDir);

        MigrateLegacyUserData();

        var seedConfig = Path.Combine(seedBaseDir, "config.json");
        if (!File.Exists(ConfigPath) && File.Exists(seedConfig))
            File.Copy(seedConfig, ConfigPath);

        MergeThemes(Path.Combine(seedBaseDir, "themes"));
    }

    /// <summary>
    /// One-off: if the shared folder has no config yet but the current user has one from an
    /// earlier version, copy their config and themes across. A no-op for LocalSystem (its
    /// LocalAppData is the system profile) and once the shared config exists.
    /// </summary>
    private static void MigrateLegacyUserData()
    {
        if (File.Exists(ConfigPath)) return;
        if (string.Equals(Path.GetFullPath(LegacyUserDataDir), Path.GetFullPath(DataDir), StringComparison.OrdinalIgnoreCase)) return;

        var legacyConfig = Path.Combine(LegacyUserDataDir, "config.json");
        if (!File.Exists(legacyConfig)) return;

        try
        {
            File.Copy(legacyConfig, ConfigPath);
            MergeThemes(Path.Combine(LegacyUserDataDir, "themes"));
        }
        catch
        {
            // Best effort — falling back to bundled defaults is fine.
        }
    }

    private static void MergeThemes(string sourceThemes)
    {
        if (!Directory.Exists(sourceThemes))
            return;

        Directory.CreateDirectory(ThemesDir);

        // JSON themes (top-level files): add any that are missing.
        foreach (var file in Directory.GetFiles(sourceThemes))
        {
            var dest = Path.Combine(ThemesDir, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Copy(file, dest);
        }

        // Folder themes (theme.yaml / theme.json + assets): add any folder that isn't already there.
        foreach (var dir in Directory.GetDirectories(sourceThemes))
        {
            var dest = Path.Combine(ThemesDir, Path.GetFileName(dir));
            if (!Directory.Exists(dest))
                CopyDirectory(dir, dest);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), overwrite: true);
    }
}
