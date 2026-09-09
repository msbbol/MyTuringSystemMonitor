using System.Drawing;
using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class MonitorLoopTests
{
    private static MonitorLoop BuildLoop(out NullDisplayDevice device)
    {
        device = new NullDisplayDevice(320, 480);
        return new MonitorLoop(
            device,
            new FakeSensorProvider(),
            new GdiThemeRenderer(),
            Theme.Default,
            TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Start_initializes_device_once()
    {
        var loop = BuildLoop(out var device);
        loop.Start();
        Assert.Equal(1, device.InitializeCount);
    }

    [Fact]
    public void RenderOnce_pushes_one_correctly_sized_frame()
    {
        var loop = BuildLoop(out var device);
        loop.Start();

        loop.RenderOnce();
        loop.RenderOnce();
        loop.RenderOnce();

        Assert.Equal(3, device.FrameCount);
        Assert.Equal(new Size(320, 480), device.LastFrameSize);
        // brightness/orientation setup happens exactly once, not per frame
        Assert.Equal(1, device.InitializeCount);
    }

    [Fact]
    public void Shutdown_clears_and_powers_off()
    {
        var loop = BuildLoop(out var device);
        loop.Start();
        loop.Shutdown();

        Assert.Equal(1, device.ClearCount);
        Assert.Equal(1, device.TurnOffCount);
    }

    [Fact]
    public async Task RunAsync_stops_on_cancellation_and_cleans_up()
    {
        var loop = BuildLoop(out var device);
        using var cts = new CancellationTokenSource();

        var run = loop.RunAsync(cts.Token);
        // let a few frames go by, then stop
        await Task.Delay(60);
        cts.Cancel();
        await run;

        Assert.True(device.FrameCount >= 1, "expected at least one frame before cancellation");
        Assert.Equal(1, device.InitializeCount);
        Assert.Equal(1, device.ClearCount);
        Assert.Equal(1, device.TurnOffCount);
    }
}
