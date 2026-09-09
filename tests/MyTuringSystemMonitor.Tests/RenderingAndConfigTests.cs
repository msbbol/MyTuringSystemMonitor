using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class RenderingAndConfigTests
{
    [Fact]
    public void GdiThemeRenderer_produces_bitmap_of_requested_size()
    {
        var renderer = new GdiThemeRenderer();
        using var bmp = renderer.Render(Theme.Default, SensorSnapshot.Empty, 320, 480);
        Assert.Equal(320, bmp.Width);
        Assert.Equal(480, bmp.Height);
    }

    [Fact]
    public void GdiThemeRenderer_fills_gauge_according_to_bound_value()
    {
        var renderer = new GdiThemeRenderer();

        // Default theme has a CPU gauge at x=20,y=138,w=280,h=24 with fill color #8AB4F8 (B=0xF8).
        var full = new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, 100.0).Build();
        var empty = new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, 0.0).Build();

        using var fullBmp = renderer.Render(Theme.Default, full, 320, 480);
        using var emptyBmp = renderer.Render(Theme.Default, empty, 320, 480);

        // A point well inside the gauge: filled (high blue) at 100%, track color (low blue) at 0%.
        var filledPixel = fullBmp.GetPixel(40, 150);
        var trackPixel = emptyBmp.GetPixel(40, 150);

        Assert.True(filledPixel.B > 200, $"expected fill blue, got {filledPixel}");
        Assert.True(trackPixel.B < 150, $"expected track color, got {trackPixel}");
    }

    [Fact]
    public void AppConfig_maps_to_display_options_and_keeps_reset_off_by_default()
    {
        var config = new AppConfig(); // defaults
        var options = config.ToDisplayOptions("COM7");

        Assert.Equal("COM7", options.PortName);
        Assert.Equal(DisplayRevision.RevisionA, options.Revision);
        Assert.Equal(DisplayOrientation.Portrait, options.Orientation);
        Assert.Equal(20, options.BrightnessPercent);
        Assert.False(options.ResetOnStartup); // the hard-won default
    }

    [Fact]
    public void AppConfig_parses_revision_and_orientation_case_insensitively()
    {
        var config = new AppConfig { Revision = "revisionc", Orientation = "landscape" };
        var options = config.ToDisplayOptions("COM4");

        Assert.Equal(DisplayRevision.RevisionC, options.Revision);
        Assert.Equal(DisplayOrientation.Landscape, options.Orientation);
    }

    [Fact]
    public void FakeSensorProvider_reports_cpu_load_in_range()
    {
        using var provider = new FakeSensorProvider();
        provider.Initialize();

        for (var i = 0; i < 20; i++)
        {
            var cpu = provider.Read().Get(SensorKeys.CpuLoad);
            Assert.NotNull(cpu);
            Assert.InRange(cpu!.Value, 0, 100);
        }
    }
}
