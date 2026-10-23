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

        // Locate the CPU-load gauge in the default theme and sample near its left edge,
        // so this test survives layout tweaks.
        var gauge = Theme.Default.Widgets.OfType<GaugeWidget>()
            .First(g => g.Sensor == SensorKeys.CpuLoad);
        var px = gauge.X + 4;
        var py = gauge.Y + gauge.Height / 2;

        var full = new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, 100.0).Build();
        var empty = new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, 0.0).Build();

        using var fullBmp = renderer.Render(Theme.Default, full, 320, 480);
        using var emptyBmp = renderer.Render(Theme.Default, empty, 320, 480);

        // Fill color #8AB4F8 has high blue; track color #1E2A38 has low blue.
        var filledPixel = fullBmp.GetPixel(px, py);
        var trackPixel = emptyBmp.GetPixel(px, py);

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

    [Theory]
    [InlineData("Portrait", 320, 480)]
    [InlineData("Landscape", 480, 320)]
    [InlineData("ReverseLandscape", 480, 320)]
    public void AppConfig_render_size_follows_orientation(string orientation, int expectedW, int expectedH)
    {
        var config = new AppConfig { Width = 320, Height = 480, Orientation = orientation };
        var (w, h) = config.RenderSize();
        Assert.Equal(expectedW, w);
        Assert.Equal(expectedH, h);
    }

    [Theory]
    // configured orientation, theme is landscape, expected — the reverse variant is preserved.
    [InlineData("Portrait", true, "Landscape")]
    [InlineData("ReversePortrait", true, "ReverseLandscape")]
    [InlineData("Landscape", false, "Portrait")]
    [InlineData("ReverseLandscape", false, "ReversePortrait")]
    [InlineData("Landscape", true, "Landscape")]
    public void AlignOrientation_rotates_onto_the_theme_axis(string configured, bool landscapeTheme, string expected)
    {
        Assert.Equal(expected, AppConfig.AlignOrientation(configured, landscapeTheme));
    }

    [Fact]
    public void EffectiveOrientation_and_render_size_follow_the_theme()
    {
        var config = new AppConfig { Width = 320, Height = 480, Orientation = "Portrait" };

        // A landscape theme rotates the panel and swaps the canvas...
        Assert.Equal("Landscape", config.EffectiveOrientation(themeIsLandscape: true));
        Assert.Equal((480, 320), config.RenderSize(themeIsLandscape: true));

        // ...while a theme with no declared axis leaves the configured orientation alone.
        Assert.Equal("Portrait", config.EffectiveOrientation(themeIsLandscape: null));
        Assert.Equal((320, 480), config.RenderSize(themeIsLandscape: null));
    }

    [Fact]
    public void ToDisplayOptions_uses_the_theme_axis()
    {
        var config = new AppConfig { Orientation = "Portrait" };
        var options = config.ToDisplayOptions("COM4", themeIsLandscape: true);
        Assert.Equal(DisplayOrientation.Landscape, options.Orientation);
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
