using System.Collections.Concurrent;
using System.Drawing.Imaging;
using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Service;

public sealed record ThemeInfo(string Name, string Kind, string? Orientation);

/// <summary>
/// Theme list and rendered previews for the web UI. Loading a theme (YAML parsing, reading the
/// background image) isn't free and there are dozens, so results are cached per file timestamp.
/// </summary>
public sealed class ThemeGallery
{
    private sealed record Cached(DateTime Stamp, ThemeInfo Info, byte[]? Preview);

    private readonly ConcurrentDictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ThemeInfo> List()
    {
        var result = new List<ThemeInfo>();
        foreach (var entry in ThemeCatalog.Discover(AppPaths.ThemesDir))
        {
            var cached = Get(entry, renderPreview: false);
            if (cached is not null) result.Add(cached.Info);
        }
        return result;
    }

    /// <summary>PNG preview rendered with demo sensor values at the theme's own orientation.</summary>
    public byte[]? Preview(string name)
    {
        var entry = ThemeCatalog.Discover(AppPaths.ThemesDir)
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : Get(entry, renderPreview: true)?.Preview;
    }

    public bool Exists(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        ThemeCatalog.Discover(AppPaths.ThemesDir).Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    private Cached? Get(ThemeEntry entry, bool renderPreview)
    {
        var stamp = Stamp(entry.Path);
        if (_cache.TryGetValue(entry.Name, out var hit) && hit.Stamp == stamp && (!renderPreview || hit.Preview is not null))
            return hit;

        try
        {
            var theme = ThemeCatalog.Load(entry, AppPaths.FontsDir);
            var info = new ThemeInfo(
                entry.Name,
                entry.Kind == ThemeKind.Yaml ? "yaml" : "json",
                theme.Orientation?.ToString().ToLowerInvariant());

            byte[]? preview = hit is not null && hit.Stamp == stamp ? hit.Preview : null;
            if (renderPreview)
                preview = Render(theme);

            var fresh = new Cached(stamp, info, preview);
            _cache[entry.Name] = fresh;
            return fresh;
        }
        catch
        {
            return null;   // a broken theme file shouldn't take the gallery down
        }
    }

    private static byte[] Render(Theme theme)
    {
        var config = new AppConfig();
        var (w, h) = config.RenderSize(MonitorFactory.IsLandscape(theme));

        using var sensors = new FakeSensorProvider();
        sensors.Initialize();
        var snapshot = sensors.Read();

        using var frame = new GdiThemeRenderer().Render(theme, snapshot, w, h);
        using var ms = new MemoryStream();
        frame.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static DateTime Stamp(string themeFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(themeFile)!;
            var newest = File.GetLastWriteTimeUtc(themeFile);
            var background = Path.Combine(dir, "background.png");
            if (File.Exists(background))
            {
                var bg = File.GetLastWriteTimeUtc(background);
                if (bg > newest) newest = bg;
            }
            return newest;
        }
        catch { return DateTime.MinValue; }
    }
}
