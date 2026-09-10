using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// Loads a <see cref="Theme"/> from JSON. Widgets are polymorphic on a "type" discriminator
/// ("text" / "gauge"); unknown types raise a clear error rather than being silently dropped.
/// </summary>
public static class ThemeLoader
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new WidgetJsonConverter());
        return options;
    }

    public static Theme Load(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json, Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    /// <param name="baseDir">Directory that a relative <c>backgroundImage</c> resolves against.</param>
    public static Theme Parse(string json, string? baseDir = null)
    {
        var dto = JsonSerializer.Deserialize<ThemeDto>(json, Options)
                  ?? throw new JsonException("Theme JSON deserialized to null.");

        string? background = null;
        if (!string.IsNullOrWhiteSpace(dto.BackgroundImage))
        {
            background = Path.IsPathRooted(dto.BackgroundImage) || baseDir is null
                ? dto.BackgroundImage
                : Path.Combine(baseDir, dto.BackgroundImage);
        }

        return new Theme
        {
            Name = dto.Name ?? "unnamed",
            Background = dto.Background ?? "#000000",
            BackgroundGradientTo = dto.BackgroundGradientTo,
            BackgroundImagePath = background,
            Widgets = dto.Widgets,
        };
    }

    /// <summary>
    /// Loads <paramref name="path"/> if it exists; otherwise returns <see cref="Theme.Default"/>.
    /// Never throws for a missing file so a first run always shows something.
    /// </summary>
    public static Theme LoadOrDefault(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return Load(path);
        return Theme.Default;
    }

    private sealed class ThemeDto
    {
        public string? Name { get; set; }
        public string? Background { get; set; }
        public string? BackgroundGradientTo { get; set; }

        /// <summary>Path to a full-screen background image, relative to the theme file.</summary>
        public string? BackgroundImage { get; set; }

        public List<Widget> Widgets { get; set; } = new();
    }

    private sealed class WidgetJsonConverter : JsonConverter<Widget>
    {
        public override Widget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var te) ? te.GetString() : null;
            var raw = root.GetRawText();

            // Deserializing to the concrete type does not re-enter this converter (it only binds to Widget).
            return type?.ToLowerInvariant() switch
            {
                "text" => JsonSerializer.Deserialize<TextWidget>(raw, options)!,
                "gauge" => JsonSerializer.Deserialize<GaugeWidget>(raw, options)!,
                "radial" => JsonSerializer.Deserialize<RadialWidget>(raw, options)!,
                "datetime" => JsonSerializer.Deserialize<DateTimeWidget>(raw, options)!,
                "panel" => JsonSerializer.Deserialize<PanelWidget>(raw, options)!,
                "clock" => JsonSerializer.Deserialize<AnalogClockWidget>(raw, options)!,
                "sparkline" => JsonSerializer.Deserialize<SparklineWidget>(raw, options)!,
                null => throw new JsonException("Widget is missing a \"type\" property."),
                _ => throw new JsonException($"Unknown widget type '{type}'. Expected text/gauge/radial/datetime/panel/clock/sparkline."),
            };
        }

        public override void Write(Utf8JsonWriter writer, Widget value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Writing themes is not supported.");
    }
}
