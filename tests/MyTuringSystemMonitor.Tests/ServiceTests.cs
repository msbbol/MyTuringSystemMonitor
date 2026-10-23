using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using MyTuringSystemMonitor.Service;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class RequestGuardTests
{
    [Theory]
    [InlineData("localhost:5321", "GET", false, true)]
    [InlineData("127.0.0.1:5321", "GET", false, true)]
    [InlineData("[::1]:5321", "GET", false, true)]
    [InlineData("LOCALHOST", "GET", false, true)]
    [InlineData("localhost:5321", "POST", true, true)]
    [InlineData("localhost:5321", "PUT", true, true)]
    // CSRF: a cross-site form/fetch can't add the custom header.
    [InlineData("localhost:5321", "POST", false, false)]
    [InlineData("localhost:5321", "PUT", false, false)]
    // DNS rebinding: attacker's hostname resolving to 127.0.0.1.
    [InlineData("evil.example:5321", "GET", false, false)]
    [InlineData("localhost.evil.example", "GET", true, false)]
    [InlineData("", "GET", false, false)]
    [InlineData(null, "GET", false, false)]
    public void Only_local_hosts_and_marked_writes_are_allowed(string? host, string method, bool header, bool allowed)
    {
        Assert.Equal(allowed, RequestGuard.IsAllowed(host, method, header));
    }
}

public class ConfigValidatorTests
{
    private static Dictionary<string, string[]> Validate(AppConfig config) =>
        ConfigValidator.Validate(config, name => name == "Turing");

    [Fact]
    public void Accepts_sensible_settings()
    {
        var config = new AppConfig { Theme = "Turing", ComPort = "COM4" };
        Assert.Empty(Validate(config));

        config.ComPort = "auto";
        Assert.Empty(Validate(config));
    }

    [Theory]
    [InlineData("comPort")]
    [InlineData("brightness")]
    [InlineData("refreshIntervalMs")]
    [InlineData("webPort")]
    [InlineData("theme")]
    [InlineData("orientation")]
    [InlineData("sensorProvider")]
    public void Rejects_bad_values(string field)
    {
        var config = new AppConfig { Theme = "Turing" };
        switch (field)
        {
            case "comPort": config.ComPort = "COM4; rm -rf"; break;
            case "brightness": config.Brightness = 150; break;
            case "refreshIntervalMs": config.RefreshIntervalMs = 5; break;
            case "webPort": config.WebPort = 80; break;
            case "theme": config.Theme = "Nope"; break;
            case "orientation": config.Orientation = "Sideways"; break;
            case "sensorProvider": config.SensorProvider = "Magic"; break;
        }

        Assert.Contains(field, Validate(config).Keys);
    }
}

public class LogBufferTests
{
    [Fact]
    public void Keeps_the_newest_entries_and_supports_tailing()
    {
        var sink = new List<string>();
        var log = new LogBuffer(capacity: 3, sink.Add);

        foreach (var i in Enumerable.Range(1, 5))
            log.Write($"line {i}");

        var all = log.Since();
        Assert.Equal(["line 3", "line 4", "line 5"], all.Select(e => e.Message));
        Assert.Equal(5, sink.Count);

        var tail = log.Since(all[1].Seq);
        Assert.Equal(["line 5"], tail.Select(e => e.Message));
    }
}

public class MonitorRuntimeTests
{
    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condition not reached.");
            await Task.Delay(25);
        }
    }

    private static MonitorRuntime Build(
        out List<NullDisplayDevice> devices,
        Func<int, bool>? failDeviceCreation = null,
        Action? onConfigLoad = null)
    {
        var created = new List<NullDisplayDevice>();
        devices = created;
        var attempts = 0;

        return new MonitorRuntime(
            loadConfig: () =>
            {
                onConfigLoad?.Invoke();
                return new AppConfig { RefreshIntervalMs = 100, SensorProvider = "Fake", DisplayProvider = "Null" };
            },
            log: new LogBuffer(),
            sensorFactory: _ => new FakeSensorProvider(),
            themeLoader: _ => Theme.Default,
            deviceFactory: (_, _, _) =>
            {
                attempts++;
                if (failDeviceCreation?.Invoke(attempts) == true)
                    throw new InvalidOperationException("COM4 is not present.");
                var device = new NullDisplayDevice();
                lock (created) created.Add(device);
                return device;
            },
            startRetryDelay: TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Runs_pauses_resumes_and_keeps_the_last_frame()
    {
        await using var runtime = Build(out var devices);

        await runtime.StartAsync();
        await WaitFor(() => runtime.Status.State == MonitorState.Running);
        Assert.NotNull(runtime.LastFramePng());
        Assert.Equal("default (built-in)", runtime.Status.Theme);

        await runtime.PauseAsync();
        Assert.Equal(MonitorState.Paused, runtime.Status.State);
        Assert.Equal(1, devices[0].TurnOffCount);   // the screen is switched off while paused

        await runtime.ResumeAsync();
        await WaitFor(() => runtime.Status.State == MonitorState.Running && devices.Count == 2);
    }

    [Fact]
    public async Task Restart_rereads_the_config()
    {
        var loads = 0;
        await using var runtime = Build(out _, onConfigLoad: () => Interlocked.Increment(ref loads));

        await runtime.StartAsync();
        await WaitFor(() => runtime.Status.State == MonitorState.Running);
        await runtime.RestartAsync();
        await WaitFor(() => runtime.Status.State == MonitorState.Running);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Keeps_retrying_when_the_display_is_missing_at_start()
    {
        // e.g. Windows starts the service before the USB screen has enumerated.
        await using var runtime = Build(out var devices, failDeviceCreation: attempt => attempt <= 2);

        await runtime.StartAsync();
        await WaitFor(() => runtime.Status.State == MonitorState.Faulted);
        Assert.Contains("COM4", runtime.Status.LastError);

        await WaitFor(() => runtime.Status.State == MonitorState.Running);
        Assert.Single(devices);
    }
}
