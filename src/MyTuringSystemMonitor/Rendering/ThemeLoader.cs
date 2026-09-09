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
        return Parse(json);
    }

    public static Theme Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<ThemeDto>(json, Options)
                  ?? throw new JsonException("Theme JSON deserialized to null.");

        return new Theme
        {
            Name = dto.Name ?? "unnamed",
            Background = dto.Background ?? "#000000",
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
                null => throw new JsonException("Widget is missing a \"type\" property."),
                _ => throw new JsonException($"Unknown widget type '{type}'. Expected 'text' or 'gauge'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, Widget value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Writing themes is not supported.");
    }
}
