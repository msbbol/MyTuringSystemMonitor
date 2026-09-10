using System.Drawing;
using System.Drawing.Imaging;
using System.Formats.Nrbf;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

// Converts themes from the stock Turing/USBMonitor desktop app (*.data, a BinaryFormatter-
// serialised usb_hid.ThemeConfig) into this app's native folder themes:
//
//     themes/<Name>/theme.json   + themes/<Name>/background.png
//
// Usage: dotnet run --project tools/VendorThemeImporter -- [<configDir>] [<themesDir>]

var configDir = args.Length > 0 ? args[0] : @"New themes\config";
var themesDir = args.Length > 1 ? args[1] : @"src\MyTuringSystemMonitor\themes";

if (!Directory.Exists(configDir)) { Console.Error.WriteLine($"No such directory: {configDir}"); return 1; }
Directory.CreateDirectory(themesDir);

var imported = 0;
var skipped = 0;

foreach (var file in Directory.GetFiles(configDir, "*.data").OrderBy(f => f))
{
    try
    {
        var result = Import(file, themesDir);
        if (result is null) { skipped++; continue; }
        Console.WriteLine(result);
        imported++;
    }
    catch (Exception ex)
    {
        skipped++;
        Console.Error.WriteLine($"{Path.GetFileName(file)}: FAILED {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine($"\nImported {imported} theme(s), skipped {skipped}.");
return 0;

static string? Import(string file, string themesDir)
{
    using var fs = File.OpenRead(file);
    if (NrbfDecoder.Decode(fs) is not ClassRecord root) return null;
    if (!root.HasMember("<name>k__BackingField")) return null;   // AppConfig.data etc.

    var name = root.GetString("<name>k__BackingField");
    if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(file);

    var landscape = root.GetBoolean("<isLanscape>k__BackingField");
    var width = landscape ? 480 : 320;
    var height = landscape ? 320 : 480;

    var folder = Path.Combine(themesDir, Sanitize(name!));
    if (Directory.Exists(folder) && !File.Exists(Path.Combine(folder, "theme.json")))
        folder += " vendor";   // don't collide with an existing (e.g. mathoudebine) theme
    Directory.CreateDirectory(folder);

    using (var composed = ComposeBackground(root, width, height))
    {
        if (composed is null) return null;
        composed.Save(Path.Combine(folder, "background.png"), ImageFormat.Png);
    }

    var widgets = new JsonArray();
    foreach (var w in ReadArcs(root)) widgets.Add(w);      // behind
    foreach (var w in ReadBars(root)) widgets.Add(w);
    foreach (var w in ReadTexts(root)) widgets.Add(w);     // on top

    var theme = new JsonObject
    {
        ["name"] = Path.GetFileName(folder),
        ["background"] = "#000000",
        ["backgroundImage"] = "background.png",
        ["widgets"] = widgets,
    };

    File.WriteAllText(
        Path.Combine(folder, "theme.json"),
        theme.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    return $"{Path.GetFileName(folder),-26} {(landscape ? "landscape" : "portrait ")} {width}x{height}  widgets={widgets.Count}";
}

/// <summary>
/// Flattens the vendor's layered background: <c>backImg</c> is the base plate and <c>osdImg</c>
/// (or <c>osdWithFontBack</c>, which has the static labels baked in) is an overlay that can carry
/// transparent regions. Drawing only the overlay leaves black holes, so composite both.
/// </summary>
static Bitmap? ComposeBackground(ClassRecord root, int width, int height)
{
    var overlay = root.GetBoolean("<useOsdWithFontBack>k__BackingField")
        ? Fitting(ImageBytes(root, "<osdWithFontBack>k__BackingField"), width, height)
        : null;
    overlay ??= Fitting(ImageBytes(root, "<osdImg>k__BackingField"), width, height);
    var basePlate = Fitting(ImageBytes(root, "<backImg>k__BackingField"), width, height);

    if (overlay is null && basePlate is null) return null;

    var canvas = new Bitmap(width, height);
    using (var g = Graphics.FromImage(canvas))
    {
        g.Clear(Color.Black);
        if (basePlate is not null) g.DrawImage(basePlate, 0, 0, width, height);
        if (overlay is not null) g.DrawImage(overlay, 0, 0, width, height);
    }

    basePlate?.Dispose();
    overlay?.Dispose();
    return canvas;
}

static Bitmap? Fitting(byte[]? bytes, int width, int height)
{
    if (bytes is null) return null;
    try
    {
        using var ms = new MemoryStream(bytes);
        var img = new Bitmap(ms);
        if (img.Width == width && img.Height == height) return img;
        img.Dispose();
        return null;
    }
    catch { return null; }
}

static byte[]? ImageBytes(ClassRecord root, string member)
{
    if (root.GetRawValue(member) is not ClassRecord bmp) return null;
    if (bmp.GetRawValue("Data") is not SZArrayRecord<byte> arr) return null;
    return arr.GetArray();
}

// ---- element readers -------------------------------------------------------------------------

static IEnumerable<JsonObject> ReadTexts(ClassRecord root)
{
    foreach (var group in Items(root, "<groupList>k__BackingField"))
    {
        if (!group.GetBoolean("<enabled>k__BackingField")) continue;
        var baseX = group.GetInt32("<baseX>k__BackingField");
        var baseY = group.GetInt32("<baseY>k__BackingField");

        if (group.GetRawValue("<itemList>k__BackingField") is not ClassRecord list) continue;
        foreach (var item in Items(list))
        {
            if (!item.GetBoolean("<enabled>k__BackingField")) continue;
            var w = TextWidget(item, "", baseX, baseY);
            if (w is not null) yield return w;
        }
    }
}

static JsonObject? TextWidget(ClassRecord item, string prefix, int baseX, int baseY)
{
    var itemName = item.GetString($"{prefix}<itemName>k__BackingField");
    if (itemName is null) return null;

    var x = baseX + item.GetInt32($"{prefix}<posX>k__BackingField");
    var y = baseY + item.GetInt32($"{prefix}<posY>k__BackingField");
    var font = item.GetRawValue($"{prefix}<fontType>k__BackingField") as ClassRecord;
    var size = font?.GetInt32("<size>k__BackingField") ?? 18;
    var color = ColorOf(font?.GetRawValue("<color>k__BackingField") as ClassRecord) ?? "#FFFFFF";

    var node = new JsonObject
    {
        ["type"] = "text",
        ["x"] = x,
        ["y"] = y,
        ["fontFamily"] = "Consolas",
        ["fontSize"] = size,
        ["pixelFontSize"] = true,
        ["bold"] = true,
        ["color"] = color,
    };

    // Clock/date elements.
    if (itemName is "Time" or "Date")
    {
        node["type"] = "datetime";
        node["format"] = itemName == "Time" ? "HH:mm" : "yyyy-MM-dd";
        return node;
    }

    // User-defined labels carry their literal text in valueStr.
    if (itemName.StartsWith("userDefine", StringComparison.OrdinalIgnoreCase))
    {
        var literal = item.GetString("valueStr");
        if (string.IsNullOrWhiteSpace(literal)) return null;
        node["label"] = literal;
        return node;
    }

    if (!Maps.Sensors.TryGetValue(itemName, out var m)) return null;
    node["sensor"] = m.Key;
    node["format"] = m.Format;
    if (m.Scale != 1) node["scale"] = m.Scale;
    if (m.Humanize) node["humanize"] = "bytes";
    node["fallback"] = "--";
    return node;
}

static IEnumerable<JsonObject> ReadBars(ClassRecord root)
{
    foreach (var bar in Items(root, "<barList>k__BackingField"))
    {
        if (!bar.GetBoolean("TItem+<enabled>k__BackingField")) continue;
        var itemName = bar.GetString("TItem+<itemName>k__BackingField");
        if (itemName is null || !Maps.Sensors.TryGetValue(itemName, out var m)) continue;

        var h = bar.GetInt32("<height>k__BackingField");
        var len = bar.GetInt32("<length>k__BackingField");
        if (h <= 0 || len <= 0) continue;

        yield return new JsonObject
        {
            ["type"] = "gauge",
            ["x"] = bar.GetInt32("TItem+<posX>k__BackingField"),
            ["y"] = bar.GetInt32("TItem+<posY>k__BackingField"),
            ["width"] = len,
            ["height"] = h,
            ["sensor"] = m.Key,
            ["min"] = 0,
            ["max"] = 100,
            ["color"] = ColorOf(bar.GetRawValue("<frontColor>k__BackingField") as ClassRecord) ?? "#FF0000",
            ["background"] = ColorOf(bar.GetRawValue("<backColor>k__BackingField") as ClassRecord) ?? "#202020",
        };
    }
}

static IEnumerable<JsonObject> ReadArcs(ClassRecord root)
{
    foreach (var arc in Items(root, "<archList>k__BackingField"))
    {
        if (!arc.GetBoolean("TItem+<enabled>k__BackingField")) continue;
        var itemName = arc.GetString("TItem+<itemName>k__BackingField");
        if (itemName is null || !Maps.Sensors.TryGetValue(itemName, out var m)) continue;

        // The vendor stores the arc's bounding box: top-left at pos, side length archR.
        var diameter = arc.GetInt32("<archR>k__BackingField");
        if (diameter <= 2) continue;
        var radius = diameter / 2;
        var cx = arc.GetInt32("TItem+<posX>k__BackingField") + radius;
        var cy = arc.GetInt32("TItem+<posY>k__BackingField") + radius;
        var thickness = (int)Math.Round(arc.GetSingle("<archWidth>k__BackingField"));
        var startPer = arc.GetInt32("<startPer>k__BackingField");

        yield return new JsonObject
        {
            ["type"] = "radial",
            ["centerX"] = cx,
            ["centerY"] = cy,
            ["radius"] = radius,
            ["thickness"] = Math.Max(2, thickness),
            ["sensor"] = m.Key,
            ["min"] = 0,
            ["max"] = 100,
            // 0% starts at 12 o'clock; startPer shifts it around the dial.
            ["angleStart"] = Norm(270 + startPer * 3.6),
            ["angleEnd"] = Norm(270 + startPer * 3.6),
            ["barColor"] = ColorOf(arc.GetRawValue("<frontArchColor>k__BackingField") as ClassRecord) ?? "#00FFFF",
            // The theme artwork already draws the track, so only the value arc is overlaid.
            ["drawTrack"] = false,
            ["roundCap"] = false,
        };
    }
}

static int Norm(double deg)
{
    var d = deg % 360;
    if (d < 0) d += 360;
    return (int)Math.Round(d);
}

// ---- helpers ---------------------------------------------------------------------------------

static IEnumerable<ClassRecord> Items(ClassRecord owner, string? member = null)
{
    var list = member is null ? owner : owner.GetRawValue(member) as ClassRecord;
    if (list is null) yield break;
    var size = list.GetInt32("_size");
    if (list.GetRawValue("_items") is not SZArrayRecord<SerializationRecord> arr) yield break;

    var i = 0;
    foreach (var e in arr.GetArray())
    {
        if (i++ >= size) break;
        if (e is ClassRecord c) yield return c;
    }
}

static string? ColorOf(ClassRecord? c)
{
    if (c is null) return null;
    try
    {
        var value = c.GetInt64("value");
        var known = c.GetInt16("knownColor");
        var state = c.GetInt16("state");

        Color color;
        if ((state & 2) != 0) color = Color.FromArgb((int)value);
        else if (known != 0) color = Color.FromKnownColor((KnownColor)known);
        else return null;

        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
    catch { return null; }
}

static string Sanitize(string name)
{
    var cleaned = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
    return cleaned.Trim().TrimEnd('.');
}

internal readonly record struct Metric(string Key, string Format, double Scale = 1, bool Humanize = false);

/// <summary>itemName in the vendor format -> this app's sensor key and display format.</summary>
internal static class Maps
{
    public static readonly Dictionary<string, Metric> Sensors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CpuLoad"] = new("cpu.load", "{0:F0}"),
        ["CpuTemp"] = new("cpu.temp", "{0:F0}"),
        ["CpuFreq"] = new("cpu.clock", "{0:F1}", Scale: 0.001),
        ["CpuPower"] = new("cpu.power", "{0:F0}"),
        ["GpuLoad"] = new("gpu.load", "{0:F0}"),
        ["GpuTemp"] = new("gpu.temp", "{0:F0}"),
        ["GpuRam"] = new("gpu.mem", "{0:F0}"),
        ["RamLoad"] = new("ram.load", "{0:F0}"),
        ["RamValue"] = new("ram.used", "{0:F0}", Scale: 1024),
        ["HDDLoad"] = new("disk.load", "{0:F0}"),
        ["InternetDown"] = new("net.down", "{0:F0}", Humanize: true),
        ["InternetUp"] = new("net.up", "{0:F0}", Humanize: true),
    };
}
