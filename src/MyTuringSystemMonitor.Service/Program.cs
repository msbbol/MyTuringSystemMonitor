using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting.WindowsServices;
using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Sensors;
using MyTuringSystemMonitor.Service;

// A Windows service starts in System32 — anchor everything to the install folder.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

AppPaths.EnsureSeeded(AppContext.BaseDirectory);
var startupConfig = ConfigLoader.LoadOrCreate(AppPaths.ConfigPath);
var webPort = startupConfig.WebPort is > 0 and < 65536 ? startupConfig.WebPort : AppConfig.DefaultWebPort;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Host.UseWindowsService(options => options.ServiceName = ServiceInfo.Name);

// The UI polls every couple of seconds; per-request logs would drown the monitor's own messages.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

// Loopback only: the control panel is for this PC, never the network.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(webPort));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Monitor");
    return new LogBuffer(500, message => logger.LogInformation("{Message}", message));
});
builder.Services.AddSingleton(sp => new MonitorRuntime(
    () => ConfigLoader.LoadOrCreate(AppPaths.ConfigPath),
    sp.GetRequiredService<LogBuffer>()));
builder.Services.AddSingleton<ThemeGallery>();
builder.Services.AddHostedService<MonitorHostedService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var request = context.Request;
    if (!RequestGuard.IsAllowed(request.Headers.Host, request.Method, request.Headers.ContainsKey(RequestGuard.HeaderName)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    // The panel only ever runs in its own origin.
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/status", (MonitorRuntime runtime) => new
{
    Monitor = runtime.Status,
    Service = new
    {
        ServiceInfo.Name,
        ServiceInfo.Version,
        RunningAsService = WindowsServiceHelpers.IsWindowsService(),
        WebPort = webPort,
        DataDir = AppPaths.DataDir,
    },
    // Memory, so "why does this service take so much RAM?" can be answered with numbers.
    // Task Manager shows the working set; the gap between it and the managed heap is memory the
    // GC is holding on to rather than memory the monitor is using.
    Memory = new
    {
        WorkingSetMb = Environment.WorkingSet / 1024 / 1024,
        ManagedHeapMb = GC.GetTotalMemory(false) / 1024 / 1024,
        HeapCommittedMb = GC.GetGCMemoryInfo().TotalCommittedBytes / 1024 / 1024,
        ServerGc = System.Runtime.GCSettings.IsServerGC,
        Gen2Collections = GC.CollectionCount(2),
        Threads = System.Diagnostics.Process.GetCurrentProcess().Threads.Count,
    },
});

api.MapGet("/config", () => ConfigLoader.LoadOrCreate(AppPaths.ConfigPath));

api.MapPut("/config", async (AppConfig config, MonitorRuntime runtime, ThemeGallery gallery) =>
{
    var errors = ConfigValidator.Validate(config, gallery);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var previous = ConfigLoader.LoadOrCreate(AppPaths.ConfigPath);
    ConfigLoader.Save(AppPaths.ConfigPath, config);
    runtime.Log.Write($"Settings saved (theme {config.Theme}, brightness {config.Brightness}%).");
    await runtime.RestartAsync();

    return Results.Ok(new { restartServiceRequired = previous.WebPort != config.WebPort });
});

api.MapGet("/themes", (ThemeGallery gallery) => gallery.List());

api.MapGet("/themes/{name}/preview", (string name, ThemeGallery gallery) =>
    gallery.Preview(name) is { } png ? Results.File(png, "image/png") : Results.NotFound());

api.MapGet("/frame", (MonitorRuntime runtime) =>
    runtime.LastFramePng() is { } png ? Results.File(png, "image/png") : Results.NoContent());

api.MapGet("/ports", () => ComPortResolver.AvailablePorts());

// Everything LibreHardwareMonitor can see on this machine, with every hardware group enabled.
// Answers "is this reading missing because the theme is wrong, or because the hardware never
// reports it?" — notably fan speeds, which many laptops don't expose at all. Opens its own
// short-lived LHM instance, so it costs nothing while it isn't called.
api.MapGet("/hardware", (MonitorRuntime runtime) =>
{
    try
    {
        // Ask the running instance when there is one: a second LHM instance would share its
        // kernel driver and take it down with it when this request finishes.
        return Results.Ok(runtime.Sensors is LibreHardwareSensorProvider live
            ? live.Dump()
            : LibreHardwareSensorProvider.DumpAll());
    }
    catch (Exception ex) { return Results.Problem($"Hardware scan failed: {ex.Message}"); }
});

// What the monitor currently reads — handy for "why is this field empty on my theme?".
api.MapGet("/sensors", (MonitorRuntime runtime) => runtime.LastSnapshot is { } snapshot
    ? Results.Ok(new
    {
        snapshot.Timestamp,
        Values = snapshot.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
        Texts = snapshot.Texts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
    })
    : Results.NoContent());

api.MapGet("/logs", (long? after, MonitorRuntime runtime) => runtime.Log.Since(after ?? 0));

api.MapPost("/control/pause", async (MonitorRuntime runtime) => { await runtime.PauseAsync(); return runtime.Status; });
api.MapPost("/control/resume", async (MonitorRuntime runtime) => { await runtime.ResumeAsync(); return runtime.Status; });
api.MapPost("/control/restart", async (MonitorRuntime runtime) => { await runtime.RestartAsync(); return runtime.Status; });

app.Run();
