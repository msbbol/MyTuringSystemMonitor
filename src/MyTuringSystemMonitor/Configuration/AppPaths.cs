namespace MyTuringSystemMonitor.Configuration;

/// <summary>
/// Shared file locations. Config and themes live in a per-user data folder so the console runner
/// and the WinForms configurator read/write the same files. Fonts are static and stay next to the
/// executable. First run seeds the data folder from the files bundled with the app.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyTuringSystemMonitor");

    public static string ConfigPath => Path.Combine(DataDir, "config.json");
    public static string ThemesDir => Path.Combine(DataDir, "themes");

    /// <summary>Font files ship next to the exe (read-only, not user-edited).</summary>
    public static string FontsDir => Path.Combine(AppContext.BaseDirectory, "fonts");

    /// <summary>
    /// Ensures the data folder exists, seeds config.json on first run, and merges any bundled
    /// themes that aren't present yet from <paramref name="seedBaseDir"/> (typically the exe dir).
    /// Existing/edited themes are left untouched, so new bundled themes appear after an upgrade
    /// without clobbering the user's own.
    /// </summary>
    public static void EnsureSeeded(string seedBaseDir)
    {
        Directory.CreateDirectory(DataDir);

        var seedConfig = Path.Combine(seedBaseDir, "config.json");
        if (!File.Exists(ConfigPath) && File.Exists(seedConfig))
            File.Copy(seedConfig, ConfigPath);

        var seedThemes = Path.Combine(seedBaseDir, "themes");
        if (!Directory.Exists(seedThemes))
            return;

        Directory.CreateDirectory(ThemesDir);

        // JSON themes (top-level files): add any that are missing.
        foreach (var file in Directory.GetFiles(seedThemes))
        {
            var dest = Path.Combine(ThemesDir, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Copy(file, dest);
        }

        // YAML themes (folders): add any folder that isn't already there.
        foreach (var dir in Directory.GetDirectories(seedThemes))
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
