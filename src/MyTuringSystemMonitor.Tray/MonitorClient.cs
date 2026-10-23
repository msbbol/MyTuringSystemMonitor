using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTuringSystemMonitor.Tray;

public sealed record MonitorInfo(
    string State,
    string? Theme,
    string? Orientation,
    string? Port,
    string DisplayProvider,
    long FramesShown,
    string? LastError);

public sealed record ServiceDetails(string Version, bool RunningAsService, int WebPort);

public sealed record StatusResponse(MonitorInfo Monitor, ServiceDetails Service);

/// <summary>Talks to the service's local control API (the same one the web UI uses).</summary>
public sealed class MonitorClient : IDisposable
{
    // Must match RequestGuard.HeaderName in the service: state-changing calls without it are refused.
    private const string ControlHeader = "X-Turing-Monitor";
    private const int DefaultWebPort = 5321;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public int Port { get; private set; } = ReadWebPort();

    public Uri PanelUri => new($"http://localhost:{Port}/");

    public async Task<StatusResponse?> GetStatusAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<StatusResponse>(new Uri(PanelUri, "api/status"), Json);
        }
        catch
        {
            // The port may have been changed in the settings — pick it up for the next poll.
            Port = ReadWebPort();
            return null;
        }
    }

    /// <summary>POSTs /api/control/{action} (pause, resume, restart). Returns false if unreachable.</summary>
    public async Task<bool> ControlAsync(string action)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(PanelUri, $"api/control/{action}"));
            request.Headers.Add(ControlHeader, "1");
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads webPort from the shared config the service uses (read-only; never creates it).</summary>
    private static int ReadWebPort()
    {
        try
        {
            var dataDir = Environment.GetEnvironmentVariable("TURING_MONITOR_DATA") is { Length: > 0 } overridden
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyTuringSystemMonitor");

            var path = Path.Combine(dataDir, "config.json");
            if (!File.Exists(path)) return DefaultWebPort;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in doc.RootElement.EnumerateObject())
                if (property.Name.Equals("webPort", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out var port) && port is > 0 and < 65536)
                    return port;
        }
        catch { /* fall back to the default */ }

        return DefaultWebPort;
    }

    public void Dispose() => _http.Dispose();
}
