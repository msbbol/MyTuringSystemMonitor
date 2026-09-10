using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class YamlAndSensorsTests
{
    [Fact]
    public void TextWidget_applies_scale_and_unit()
    {
        // MHz -> GHz: scale 0.001, unit " GHz".
        var widget = new TextWidget { Sensor = SensorKeys.CpuClock, Format = "{0:F2}", Scale = 0.001, Unit = " GHz" };
        var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.CpuClock, 2400.0).Build();
        Assert.Equal("2.40 GHz", widget.ResolveText(snapshot));
    }

    [Fact]
    public void MathoudebineThemeLoader_maps_yaml_to_widgets()
    {
        const string yaml = """
        static_images:
          BACKGROUND: { PATH: background.png, X: 0, Y: 0, WIDTH: 320, HEIGHT: 480 }
        static_text:
          DISK_USED_LABEL: { TEXT: "Used:", X: 1, Y: 2, FONT_SIZE: 23, FONT_COLOR: "255, 255, 255" }
        STATS:
          CPU:
            PERCENTAGE:
              TEXT:  { SHOW: True, SHOW_UNIT: True, X: 250, Y: 24, FONT_SIZE: 23, FONT_COLOR: "255, 255, 255" }
              GRAPH: { SHOW: True, X: 155, Y: 55, WIDTH: 150, HEIGHT: 15, MIN_VALUE: 0, MAX_VALUE: 100, BAR_COLOR: "0, 0, 255", BACKGROUND_COLOR: "0, 0, 0" }
          DATE:
            DAY:
              TEXT: { SHOW: True, X: 155, Y: 6, FONT_SIZE: 10, FONT_COLOR: "255, 255, 255" }
        """;

        var dir = Path.Combine(Path.GetTempPath(), "mtsm-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "theme.yaml");
        File.WriteAllText(path, yaml);

        try
        {
            var theme = MathoudebineThemeLoader.Load(path, fontsDir: dir);

            // Static label.
            var label = theme.Widgets.OfType<TextWidget>().First(w => w.Label == "Used:");
            Assert.Equal(1, label.X);
            Assert.True(label.PixelFontSize);

            // CPU percentage text bound to cpu.load with a "%" unit.
            var cpu = theme.Widgets.OfType<TextWidget>().First(w => w.Sensor == SensorKeys.CpuLoad);
            Assert.Equal("%", cpu.Unit);
            Assert.Equal(250, cpu.X);

            // CPU graph -> gauge bound to cpu.load.
            var gauge = theme.Widgets.OfType<GaugeWidget>().First(w => w.Sensor == SensorKeys.CpuLoad);
            Assert.Equal(150, gauge.Width);
            Assert.Equal("#0000FF", gauge.Color);

            // Date widget present.
            Assert.Contains(theme.Widgets, w => w is DateTimeWidget);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    [InlineData(200, 1.0)] // clamped
    public void RadialWidget_resolves_clamped_fraction(double value, double expected)
    {
        var widget = new RadialWidget { Sensor = SensorKeys.CpuLoad, Min = 0, Max = 100 };
        var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.CpuLoad, value).Build();
        Assert.Equal(expected, widget.ResolveFraction(snapshot), 3);
    }

    [Fact]
    public void SystemMetrics_reports_disk_usage()
    {
        var builder = new SensorSnapshot.Builder();
        SystemMetrics.AppendDisk(builder);
        var snapshot = builder.Build();

        var total = snapshot.Get(SensorKeys.DiskTotal);
        var load = snapshot.Get(SensorKeys.DiskLoad);

        Assert.NotNull(total);
        Assert.True(total > 0);
        Assert.NotNull(load);
        Assert.InRange(load!.Value, 0, 100);
    }

    [Theory]
    [InlineData(7_400_000, "↓ 7.4M")]
    [InlineData(860_000, "↓ 860K")]
    [InlineData(2_100_000_000, "↓ 2.1G")]
    [InlineData(512, "↓ 512")]
    public void TextWidget_humanizes_byte_rates(double bytes, string expected)
    {
        var widget = new TextWidget { Label = "↓ ", Sensor = SensorKeys.NetDown, Humanize = "bytes" };
        var snapshot = new SensorSnapshot.Builder().Set(SensorKeys.NetDown, bytes).Build();
        Assert.Equal(expected, widget.ResolveText(snapshot));
    }

    [Fact]
    public void TextWidget_binds_to_text_sensor()
    {
        var widget = new TextWidget { TextSensor = SensorKeys.ProcTopName, Fallback = "--" };
        var withValue = new SensorSnapshot.Builder().SetText(SensorKeys.ProcTopName, "firefox").Build();
        Assert.Equal("firefox", widget.ResolveText(withValue));
        Assert.Equal("--", widget.ResolveText(SensorSnapshot.Empty));
    }

    [Fact]
    public void SystemMetrics_reports_clock_pseudo_sensors()
    {
        var builder = new SensorSnapshot.Builder();
        SystemMetrics.AppendTime(builder);
        var s = builder.Build();

        Assert.InRange(s.Get(SensorKeys.TimeSecond)!.Value, 0, 60);
        Assert.InRange(s.Get(SensorKeys.TimeMinute)!.Value, 0, 60);
        Assert.InRange(s.Get(SensorKeys.TimeHour12)!.Value, 0, 12);
        Assert.True(s.Get(SensorKeys.UptimeHours)!.Value >= 0);
    }

    [Fact]
    public void ThemeLoader_parses_gradient_and_custom_widget_types()
    {
        const string json = """
        {
          "name": "t",
          "background": "#111111",
          "backgroundGradientTo": "#222222",
          "widgets": [
            { "type": "panel", "x": 1, "y": 2, "width": 10, "height": 20, "fill": "#333333", "cornerRadius": 5 },
            { "type": "clock", "centerX": 50, "centerY": 60, "radius": 30 },
            { "type": "datetime", "format": "HH:mm", "x": 1, "y": 1 },
            { "type": "radial", "centerX": 5, "centerY": 6, "radius": 10, "sensor": "cpu.load" },
            { "type": "sparkline", "x": 5, "y": 6, "width": 40, "height": 20, "sensor": "cpu.load", "style": "line" }
          ]
        }
        """;

        var theme = ThemeLoader.Parse(json);
        Assert.Equal("#222222", theme.BackgroundGradientTo);
        Assert.Contains(theme.Widgets, w => w is PanelWidget);
        Assert.Contains(theme.Widgets, w => w is AnalogClockWidget);
        Assert.Contains(theme.Widgets, w => w is DateTimeWidget);
        Assert.Contains(theme.Widgets, w => w is RadialWidget);
        Assert.Contains(theme.Widgets, w => w is SparklineWidget);
    }

    [Fact]
    public void ThemeCatalog_discovers_json_and_yaml_themes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mtsm-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "FancyTheme"));
        File.WriteAllText(Path.Combine(dir, "mine.json"), """{ "name": "mine", "widgets": [] }""");
        File.WriteAllText(Path.Combine(dir, "FancyTheme", "theme.yaml"), "STATS: {}\n");

        try
        {
            var entries = ThemeCatalog.Discover(dir);
            Assert.Contains(entries, e => e.Name == "mine" && e.Kind == ThemeKind.Json);
            Assert.Contains(entries, e => e.Name == "FancyTheme" && e.Kind == ThemeKind.Yaml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
