using MyTuringSystemMonitor.App;

namespace MyTuringSystemMonitor.Service;

/// <summary>Starts the monitor with the host and turns the screen off when the service stops.</summary>
public sealed class MonitorHostedService(MonitorRuntime runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        runtime.Log.Write("Service started.");
        return runtime.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        runtime.Log.Write("Service stopping.");
        await runtime.DisposeAsync();
    }
}
