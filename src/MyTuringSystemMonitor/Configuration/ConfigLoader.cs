using System.Text.Json;

namespace MyTuringSystemMonitor.Configuration;

/// <summary>Loads <see cref="AppConfig"/> from JSON, writing a default file on first run.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads config from <paramref name="path"/>. If it doesn't exist, a default file is written
    /// there (so the user has something to edit) and the defaults are returned.
    /// </summary>
    public static AppConfig LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, ReadOptions) ?? new AppConfig();
        }

        var config = new AppConfig();
        try { File.WriteAllText(path, JsonSerializer.Serialize(config, WriteOptions)); }
        catch { /* read-only dir: fall back to in-memory defaults */ }
        return config;
    }
}
