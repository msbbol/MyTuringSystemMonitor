namespace MyTuringSystemMonitor.Rendering;

/// <summary>A theme available to pick, discovered under the themes directory.</summary>
public sealed record ThemeEntry(string Name, ThemeKind Kind, string Path, string? PreviewImagePath);

public enum ThemeKind
{
    /// <summary>This app's native JSON theme (a *.json file).</summary>
    Json,

    /// <summary>A mathoudebine folder theme (contains theme.yaml).</summary>
    Yaml,
}

/// <summary>
/// Discovers and loads themes from a directory: native <c>*.json</c> files and mathoudebine
/// folder themes (any subfolder containing <c>theme.yaml</c>).
/// </summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<ThemeEntry> Discover(string themesDir)
    {
        var entries = new List<ThemeEntry>();
        if (!Directory.Exists(themesDir))
            return entries;

        foreach (var json in Directory.GetFiles(themesDir, "*.json", SearchOption.TopDirectoryOnly))
            entries.Add(new ThemeEntry(System.IO.Path.GetFileNameWithoutExtension(json), ThemeKind.Json, json, null));

        foreach (var dir in Directory.GetDirectories(themesDir))
        {
            var preview = new[] { "preview.png", "background.png" }
                .Select(f => System.IO.Path.Combine(dir, f))
                .FirstOrDefault(File.Exists);
            var name = new DirectoryInfo(dir).Name;

            // A folder theme is either a mathoudebine theme.yaml or a native theme.json
            // (the latter lets a JSON theme sit next to its background image).
            var yaml = System.IO.Path.Combine(dir, "theme.yaml");
            if (File.Exists(yaml))
            {
                entries.Add(new ThemeEntry(name, ThemeKind.Yaml, yaml, preview));
                continue;
            }

            var json = System.IO.Path.Combine(dir, "theme.json");
            if (File.Exists(json))
                entries.Add(new ThemeEntry(name, ThemeKind.Json, json, preview));
        }

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static Theme Load(ThemeEntry entry, string fontsDir) => entry.Kind switch
    {
        ThemeKind.Yaml => MathoudebineThemeLoader.Load(entry.Path, fontsDir),
        _ => ThemeLoader.Load(entry.Path),
    };

    /// <summary>
    /// Loads the theme named <paramref name="name"/> from <paramref name="themesDir"/>. Falls back
    /// to a direct path (legacy config), then to the built-in <see cref="Theme.Default"/>.
    /// </summary>
    public static Theme LoadByName(string themesDir, string? name, string fontsDir)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var entry = Discover(themesDir).FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                return Load(entry, fontsDir);

            // Legacy: config.Theme was a file path.
            if (File.Exists(name))
                return name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                    ? MathoudebineThemeLoader.Load(name, fontsDir)
                    : ThemeLoader.Load(name);
        }

        return Theme.Default;
    }
}
