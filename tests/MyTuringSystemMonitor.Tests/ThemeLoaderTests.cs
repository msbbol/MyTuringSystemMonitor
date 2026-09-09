using MyTuringSystemMonitor.Rendering;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class ThemeLoaderTests
{
    [Fact]
    public void Parse_reads_polymorphic_widgets()
    {
        const string json = """
        {
          "name": "test",
          "background": "#101010",
          "widgets": [
            { "type": "text",  "x": 5, "y": 6, "label": "CPU", "sensor": "cpu.load", "format": "{0:F0}%" },
            { "type": "gauge", "x": 5, "y": 40, "width": 100, "height": 10, "sensor": "cpu.load", "min": 0, "max": 100 }
          ]
        }
        """;

        var theme = ThemeLoader.Parse(json);

        Assert.Equal("test", theme.Name);
        Assert.Equal("#101010", theme.Background);
        Assert.Equal(2, theme.Widgets.Count);

        var text = Assert.IsType<TextWidget>(theme.Widgets[0]);
        Assert.Equal(5, text.X);
        Assert.Equal("CPU", text.Label);
        Assert.Equal("cpu.load", text.Sensor);

        var gauge = Assert.IsType<GaugeWidget>(theme.Widgets[1]);
        Assert.Equal(100, gauge.Width);
        Assert.Equal("cpu.load", gauge.Sensor);
    }

    [Fact]
    public void Parse_rejects_unknown_widget_type()
    {
        const string json = """{ "widgets": [ { "type": "hologram" } ] }""";
        Assert.ThrowsAny<Exception>(() => ThemeLoader.Parse(json));
    }

    [Fact]
    public void LoadOrDefault_returns_builtin_when_file_missing()
    {
        var theme = ThemeLoader.LoadOrDefault("does-not-exist.json");
        Assert.Same(Theme.Default, theme);
        Assert.NotEmpty(theme.Widgets);
    }

    [Fact]
    public void Shipped_default_theme_file_parses()
    {
        // The theme copied next to the test/app binaries must be valid.
        var path = Path.Combine(AppContext.BaseDirectory, "themes", "default.json");
        Assert.True(File.Exists(path), $"expected shipped theme at {path}");

        var theme = ThemeLoader.Load(path);
        Assert.NotEmpty(theme.Widgets);
    }
}
