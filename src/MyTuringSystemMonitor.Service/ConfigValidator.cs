using MyTuringSystemMonitor.Configuration;

namespace MyTuringSystemMonitor.Service;

/// <summary>Checks settings coming from the web UI before they're written to config.json.</summary>
public static class ConfigValidator
{
    private static readonly string[] Revisions = ["RevisionA", "RevisionB", "RevisionC", "RevisionE"];
    private static readonly string[] Orientations = ["Portrait", "ReversePortrait", "Landscape", "ReverseLandscape"];
    private static readonly string[] SensorProviders = ["Lhm", "PerfCounter", "Fake"];
    private static readonly string[] DisplayProviders = ["Turing", "Null"];

    public static Dictionary<string, string[]> Validate(AppConfig config, ThemeGallery gallery) =>
        Validate(config, gallery.Exists);

    public static Dictionary<string, string[]> Validate(AppConfig config, Func<string?, bool> themeExists)
    {
        var errors = new Dictionary<string, string[]>();
        void Fail(string field, string message) => errors[field] = [message];

        if (string.IsNullOrWhiteSpace(config.ComPort) ||
            !(config.ComPort.Equals(ComPortResolverAuto, StringComparison.OrdinalIgnoreCase) ||
              System.Text.RegularExpressions.Regex.IsMatch(config.ComPort, @"^COM\d{1,3}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            Fail("comPort", "Use AUTO or a port like COM4.");

        if (!Revisions.Contains(config.Revision, StringComparer.OrdinalIgnoreCase))
            Fail("revision", "Unknown screen revision.");

        if (!Orientations.Contains(config.Orientation, StringComparer.OrdinalIgnoreCase))
            Fail("orientation", "Unknown orientation.");

        if (!SensorProviders.Contains(config.SensorProvider, StringComparer.OrdinalIgnoreCase))
            Fail("sensorProvider", "Unknown sensor provider.");

        if (!DisplayProviders.Contains(config.DisplayProvider, StringComparer.OrdinalIgnoreCase))
            Fail("displayProvider", "Unknown display provider.");

        if (config.Brightness is < 0 or > 100)
            Fail("brightness", "Brightness must be between 0 and 100.");

        if (config.RefreshIntervalMs is < 100 or > 60_000)
            Fail("refreshIntervalMs", "Refresh interval must be between 100 and 60000 ms.");

        if (config.WebPort is < 1024 or > 65535)
            Fail("webPort", "Web port must be between 1024 and 65535.");

        if (!themeExists(config.Theme))
            Fail("theme", "Theme not found.");

        return errors;
    }

    private const string ComPortResolverAuto = MyTuringSystemMonitor.Hardware.ComPortResolver.Auto;
}
