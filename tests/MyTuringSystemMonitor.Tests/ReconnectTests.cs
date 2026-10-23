using System.Drawing;
using MyTuringSystemMonitor.App;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class ReconnectTests
{
    /// <summary>Simulates a display that drops off the bus (laptop sleep) and comes back later.</summary>
    private sealed class FlakyDisplayDevice : IDisplayDevice
    {
        public int Width => 320;
        public int Height => 480;

        public bool Connected { get; set; } = true;
        public int ReconnectsBeforeSuccess { get; set; }
        public int ReconnectCalls { get; private set; }
        public int FramesShown { get; private set; }
        public int ClearCalls { get; private set; }

        public void Initialize() { }

        public void Display(Bitmap frame)
        {
            if (!Connected) throw new InvalidOperationException("The port is closed.");
            FramesShown++;
        }

        public bool Reconnect()
        {
            ReconnectCalls++;
            if (ReconnectCalls <= ReconnectsBeforeSuccess) return false;
            Connected = true;
            return true;
        }

        public void Clear() => ClearCalls++;
        public void TurnOff() { }
        public void Dispose() { }
    }

    private static (MonitorLoop loop, FlakyDisplayDevice device, Func<DateTime> clockRef, Action<TimeSpan> advance, List<string> log)
        Build()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var device = new FlakyDisplayDevice();
        var log = new List<string>();
        var loop = new MonitorLoop(device, new FakeSensorProvider(), new GdiThemeRenderer(), Theme.Default,
            TimeSpan.FromSeconds(1), log.Add, () => now);
        return (loop, device, () => now, d => now += d, log);
    }

    [Fact]
    public void Lost_display_is_reconnected_and_frames_resume()
    {
        var (loop, device, _, _, log) = Build();

        loop.Tick();
        Assert.Equal(1, device.FramesShown);

        device.Connected = false;          // laptop goes to sleep, USB loses power
        loop.Tick();
        Assert.True(loop.DisplayLost);
        Assert.Single(log, l => l.Contains("connection lost"));

        loop.Tick();                        // device is back on the bus
        Assert.False(loop.DisplayLost);
        Assert.Equal(1, device.ReconnectCalls);
        Assert.Equal(2, device.FramesShown);
    }

    [Fact]
    public void Reconnect_backs_off_between_attempts_and_does_not_spam()
    {
        var (loop, device, _, advance, log) = Build();
        device.ReconnectsBeforeSuccess = 2;

        device.Connected = false;
        loop.Tick();                        // failure detected
        loop.Tick();                        // attempt 1 (immediate) fails -> wait 2 s
        Assert.Equal(1, device.ReconnectCalls);

        loop.Tick();                        // still inside the back-off window: no attempt
        advance(TimeSpan.FromSeconds(1));
        loop.Tick();
        Assert.Equal(1, device.ReconnectCalls);

        advance(TimeSpan.FromSeconds(1));   // 2 s elapsed -> attempt 2 fails -> wait 4 s
        loop.Tick();
        Assert.Equal(2, device.ReconnectCalls);

        advance(TimeSpan.FromSeconds(3));
        loop.Tick();
        Assert.Equal(2, device.ReconnectCalls);

        advance(TimeSpan.FromSeconds(1));   // 4 s elapsed -> attempt 3 succeeds
        loop.Tick();
        Assert.Equal(3, device.ReconnectCalls);
        Assert.False(loop.DisplayLost);
        Assert.Equal(1, device.FramesShown);

        // Only one "lost" line, not one per frame.
        Assert.Single(log, l => l.Contains("connection lost"));
        Assert.Contains(log, l => l.Contains("reconnected after 3 attempts"));
    }

    [Fact]
    public void Shutdown_skips_talking_to_a_lost_display()
    {
        var (loop, device, _, _, _) = Build();
        device.ReconnectsBeforeSuccess = int.MaxValue;
        device.Connected = false;

        loop.Tick();
        loop.Shutdown();

        Assert.Equal(0, device.ClearCalls);
    }

    [Theory]
    [InlineData("COM4", "COM4", new[] { "COM4" }, "COM4")]            // still there
    [InlineData("COM4", "COM4", new[] { "COM7" }, "COM7")]            // moved after wake
    [InlineData("COM4", "COM4", new[] { "COM3", "COM7" }, "COM4")]    // ambiguous: keep configured
    [InlineData("AUTO", "COM4", new[] { "COM9" }, "COM9")]
    [InlineData("AUTO", "COM4", new string[0], "COM4")]              // not back yet: keep last
    public void ResolveForReconnect_follows_the_device(string configured, string last, string[] available, string expected)
    {
        Assert.Equal(expected, ComPortResolver.ResolveForReconnect(configured, last, available));
    }
}
