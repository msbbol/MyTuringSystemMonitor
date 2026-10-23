using System.Globalization;
using MyTuringSystemMonitor.Sensors;
using YamlDotNet.Serialization;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// Loads a mathoudebine-style <c>theme.yaml</c> (turing-smart-screen-python) into this app's
/// <see cref="Theme"/> model. Supports a practical subset: the full-screen background image,
/// static_text labels, per-stat TEXT/GRAPH widgets, and DATE day/hour — mapped onto this app's
/// <see cref="SensorKeys"/>. Graphs/radials beyond simple horizontal bars are not yet rendered.
/// </summary>
public static class MathoudebineThemeLoader
{
    /// <param name="themeYamlPath">Path to a theme's theme.yaml.</param>
    /// <param name="fontsDir">Directory that FONT paths (e.g. "jetbrains-mono/..") resolve against.</param>
    public static Theme Load(string themeYamlPath, string fontsDir)
    {
        var themeDir = Path.GetDirectoryName(Path.GetFullPath(themeYamlPath))!;
        var yaml = File.ReadAllText(themeYamlPath);
        var root = new DeserializerBuilder().Build().Deserialize<object>(yaml);

        var widgets = new List<Widget>();

        // Background image.
        string? backgroundPath = null;
        var background = Get(Get(Get(root, "static_images"), "BACKGROUND"), "PATH");
        if (background is not null)
        {
            var candidate = Path.Combine(themeDir, Str(background)!);
            if (File.Exists(candidate)) backgroundPath = candidate;
        }

        // Static labels.
        if (Get(root, "static_text") is { } staticText)
            foreach (var node in Values(staticText))
                widgets.Add(MakeLabel(node, fontsDir));

        var stats = Get(root, "STATS");
        if (stats is not null)
            AddStats(stats, widgets, fontsDir);

        return new Theme
        {
            Name = new DirectoryInfo(themeDir).Name,
            Orientation = ThemeOrientationHelper.Parse(Str(Get(Get(root, "display"), "DISPLAY_ORIENTATION")))
                          ?? ThemeOrientationHelper.InferFromImage(backgroundPath),
            Background = "#000000",
            BackgroundImagePath = backgroundPath,
            Widgets = widgets,
        };
    }

    private static void AddStats(object stats, List<Widget> widgets, string fontsDir)
    {
        var cpu = Get(stats, "CPU");
        AddGraph(widgets, Get(Get(cpu, "PERCENTAGE"), "GRAPH"), SensorKeys.CpuLoad);
        AddRadial(widgets, Get(Get(cpu, "PERCENTAGE"), "RADIAL"), SensorKeys.CpuLoad, fontsDir);
        AddText(widgets, Get(Get(cpu, "PERCENTAGE"), "TEXT"), SensorKeys.CpuLoad, "{0:F0}", "%", 1, fontsDir);
        AddText(widgets, Get(Get(cpu, "FREQUENCY"), "TEXT"), SensorKeys.CpuClock, "{0:F2}", " GHz", 0.001, fontsDir);
        AddText(widgets, Get(Get(cpu, "TEMPERATURE"), "TEXT"), SensorKeys.CpuTemp, "{0:F0}", "°C", 1, fontsDir);

        var gpu = Get(stats, "GPU");
        AddGraph(widgets, Get(Get(gpu, "PERCENTAGE"), "GRAPH"), SensorKeys.GpuLoad);
        AddRadial(widgets, Get(Get(gpu, "PERCENTAGE"), "RADIAL"), SensorKeys.GpuLoad, fontsDir);
        AddText(widgets, Get(Get(gpu, "PERCENTAGE"), "TEXT"), SensorKeys.GpuLoad, "{0:F0}", "%", 1, fontsDir);
        AddText(widgets, Get(Get(gpu, "MEMORY"), "TEXT"), SensorKeys.GpuMemoryUsed, "{0:F0}", " M", 1, fontsDir);
        AddText(widgets, Get(Get(gpu, "TEMPERATURE"), "TEXT"), SensorKeys.GpuTemp, "{0:F0}", "°C", 1, fontsDir);

        var mem = Get(Get(stats, "MEMORY"), "VIRTUAL");
        AddGraph(widgets, Get(mem, "GRAPH"), SensorKeys.RamLoad);
        AddRadial(widgets, Get(mem, "RADIAL"), SensorKeys.RamLoad, fontsDir);
        AddText(widgets, Get(mem, "PERCENT_TEXT"), SensorKeys.RamLoad, "{0:F0}", "%", 1, fontsDir);
        // Sizes are shown in MB ("M"); the sensors are in GB.
        AddText(widgets, Get(mem, "USED"), SensorKeys.RamUsed, "{0:F0}", " M", 1024, fontsDir);
        AddText(widgets, Get(mem, "FREE"), SensorKeys.RamFree, "{0:F0}", " M", 1024, fontsDir);
        AddText(widgets, Get(mem, "TOTAL"), SensorKeys.RamTotal, "{0:F0}", " M", 1024, fontsDir);

        var swap = Get(Get(stats, "MEMORY"), "SWAP");
        AddGraph(widgets, Get(swap, "GRAPH"), SensorKeys.SwapLoad);
        AddRadial(widgets, Get(swap, "RADIAL"), SensorKeys.SwapLoad, fontsDir);
        AddText(widgets, Get(swap, "PERCENT_TEXT"), SensorKeys.SwapLoad, "{0:F0}", "%", 1, fontsDir);
        AddText(widgets, Get(swap, "USED"), SensorKeys.SwapUsed, "{0:F0}", " M", 1024, fontsDir);

        var disk = Get(stats, "DISK");
        AddGraph(widgets, Get(Get(disk, "USED"), "GRAPH"), SensorKeys.DiskLoad);
        AddRadial(widgets, Get(Get(disk, "USED"), "RADIAL"), SensorKeys.DiskLoad, fontsDir);
        AddText(widgets, Get(Get(disk, "USED"), "TEXT"), SensorKeys.DiskUsed, "{0:F0}", " G", 1, fontsDir);
        AddText(widgets, Get(Get(disk, "TOTAL"), "TEXT"), SensorKeys.DiskTotal, "{0:F0}", " G", 1, fontsDir);
        AddText(widgets, Get(Get(disk, "FREE"), "TEXT"), SensorKeys.DiskFree, "{0:F0}", " G", 1, fontsDir);

        var date = Get(stats, "DATE");
        AddDateTime(widgets, Get(Get(date, "DAY"), "TEXT"), "MMM d, yyyy", fontsDir);
        AddDateTime(widgets, Get(Get(date, "HOUR"), "TEXT"), "h:mm:ss tt", fontsDir);
    }

    private static void AddText(List<Widget> widgets, object? node, string sensorKey, string format, string unit, double scale, string fontsDir)
    {
        if (node is null || !Show(node)) return;
        var (fontFile, size, color, anchor) = FontProps(node, fontsDir);
        widgets.Add(new TextWidget
        {
            X = Int(Get(node, "X")),
            Y = Int(Get(node, "Y")),
            Sensor = sensorKey,
            Format = format,
            Unit = Bool(Get(node, "SHOW_UNIT"), false) ? unit : string.Empty,
            Scale = scale,
            FontSize = size,
            FontFile = fontFile,
            PixelFontSize = true,
            Bold = false, // the .ttf already carries the weight
            Color = color,
            Anchor = anchor,
        });
    }

    private static void AddDateTime(List<Widget> widgets, object? node, string format, string fontsDir)
    {
        if (node is null || !Show(node)) return;
        var (fontFile, size, color, anchor) = FontProps(node, fontsDir);
        widgets.Add(new DateTimeWidget
        {
            X = Int(Get(node, "X")),
            Y = Int(Get(node, "Y")),
            Format = format,
            FontSize = size,
            FontFile = fontFile,
            PixelFontSize = true,
            Bold = false,
            Color = color,
            Anchor = anchor,
        });
    }

    private static void AddRadial(List<Widget> widgets, object? node, string sensorKey, string fontsDir)
    {
        if (node is null || !Show(node)) return;

        widgets.Add(new RadialWidget
        {
            CenterX = Int(Get(node, "X")),
            CenterY = Int(Get(node, "Y")),
            Radius = Int(Get(node, "RADIUS"), 50),
            Thickness = Int(Get(node, "WIDTH"), 10),
            Sensor = sensorKey,
            Min = Int(Get(node, "MIN_VALUE"), 0),
            Max = Int(Get(node, "MAX_VALUE"), 100),
            AngleStart = Int(Get(node, "ANGLE_START"), 270),
            AngleEnd = Int(Get(node, "ANGLE_END"), 270),
            Clockwise = Bool(Get(node, "CLOCKWISE"), true),
            BarColor = ColorHtml(Get(node, "BAR_COLOR"), "#FFFFFF"),
            TrackColor = ColorHtml(Get(node, "BAR_BACKGROUND_COLOR"), "#000000"),
            DrawTrack = Bool(Get(node, "DRAW_BAR_BACKGROUND"), true),
            RoundCap = string.Equals(Str(Get(node, "BAR_DECORATION")), "Circle", StringComparison.OrdinalIgnoreCase),
        });

        // Centred value text inside the radial.
        if (Bool(Get(node, "SHOW_TEXT"), false))
        {
            var (fontFile, size, color, _) = FontProps(node, fontsDir);
            widgets.Add(new TextWidget
            {
                X = Int(Get(node, "X")),
                Y = Int(Get(node, "Y")),
                Sensor = sensorKey,
                Format = "{0:F0}",
                Unit = Bool(Get(node, "SHOW_UNIT"), false) ? "%" : string.Empty,
                FontSize = size,
                FontFile = fontFile,
                PixelFontSize = true,
                Bold = false,
                Color = color,
                Anchor = "mm",
            });
        }
    }

    private static void AddGraph(List<Widget> widgets, object? node, string sensorKey)
    {
        if (node is null || !Show(node)) return;
        widgets.Add(new GaugeWidget
        {
            X = Int(Get(node, "X")),
            Y = Int(Get(node, "Y")),
            Width = Int(Get(node, "WIDTH"), 100),
            Height = Int(Get(node, "HEIGHT"), 15),
            Sensor = sensorKey,
            Min = Int(Get(node, "MIN_VALUE"), 0),
            Max = Int(Get(node, "MAX_VALUE"), 100),
            Color = ColorHtml(Get(node, "BAR_COLOR"), "#0000FF"),
            Background = ColorHtml(Get(node, "BACKGROUND_COLOR"), "#000000"),
        });
    }

    private static TextWidget MakeLabel(object node, string fontsDir)
    {
        var (fontFile, size, color, anchor) = FontProps(node, fontsDir);
        return new TextWidget
        {
            X = Int(Get(node, "X")),
            Y = Int(Get(node, "Y")),
            Label = Str(Get(node, "TEXT")) ?? string.Empty,
            FontSize = size,
            FontFile = fontFile,
            PixelFontSize = true,
            Bold = false,
            Color = color,
            Anchor = anchor,
        };
    }

    private static (string? fontFile, int size, string color, string? anchor) FontProps(object node, string fontsDir)
    {
        var fontRel = Str(Get(node, "FONT"));
        var fontFile = fontRel is null ? null : Path.Combine(fontsDir, fontRel.Replace('/', Path.DirectorySeparatorChar));
        var size = Int(Get(node, "FONT_SIZE"), 20);
        var color = ColorHtml(Get(node, "FONT_COLOR"), "#FFFFFF");
        var anchor = Str(Get(node, "ANCHOR"));
        return (fontFile, size, color, anchor);
    }

    // ---- YAML navigation helpers --------------------------------------------------------------

    private static object? Get(object? node, string key)
    {
        if (node is IDictionary<object, object> map)
            foreach (var kv in map)
                if (string.Equals(kv.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
        return null;
    }

    private static IEnumerable<object> Values(object? node) =>
        node is IDictionary<object, object> map ? map.Values.Where(v => v is not null)! : Enumerable.Empty<object>();

    private static string? Str(object? o) => o?.ToString();

    private static int Int(object? o, int fallback = 0) =>
        int.TryParse(Str(o), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    private static bool Bool(object? o, bool fallback) =>
        bool.TryParse(Str(o), out var b) ? b : fallback;

    private static bool Show(object? node) => Bool(Get(node, "SHOW"), true);

    private static string ColorHtml(object? o, string fallback)
    {
        var s = Str(o);
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var parts = s.Split(',');
        if (parts.Length < 3) return fallback;
        if (int.TryParse(parts[0].Trim(), out var r) &&
            int.TryParse(parts[1].Trim(), out var g) &&
            int.TryParse(parts[2].Trim(), out var b))
            return $"#{r:X2}{g:X2}{b:X2}";
        return fallback;
    }
}
