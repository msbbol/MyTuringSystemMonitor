using MyTuringSystemMonitor.Rendering;
using MyTuringSystemMonitor.Sensors;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class SensorBindingTests
{
    private static SensorSnapshot Snapshot(params (string key, double? value)[] values)
    {
        var b = new SensorSnapshot.Builder();
        foreach (var (key, value) in values)
            b.Set(key, value);
        return b.Build();
    }

    [Fact]
    public void TextWidget_formats_bound_value_with_invariant_culture()
    {
        var widget = new TextWidget { Sensor = SensorKeys.CpuLoad, Format = "{0:F1}%" };
        // Invariant culture uses '.' as the decimal separator (not ',').
        var text = widget.ResolveText(Snapshot((SensorKeys.CpuLoad, 42.28)));
        Assert.Equal("42.3%", text);
    }

    [Fact]
    public void TextWidget_prepends_label_to_value()
    {
        var widget = new TextWidget { Label = "RAM ", Sensor = SensorKeys.RamLoad, Format = "{0:F0}%" };
        Assert.Equal("RAM 63%", widget.ResolveText(Snapshot((SensorKeys.RamLoad, 63.0))));
    }

    [Fact]
    public void TextWidget_uses_fallback_when_value_missing()
    {
        var widget = new TextWidget { Sensor = SensorKeys.GpuTemp, Format = "{0:F0}", Fallback = "n/a" };
        Assert.Equal("n/a", widget.ResolveText(SensorSnapshot.Empty));
    }

    [Fact]
    public void TextWidget_with_no_sensor_is_static_label()
    {
        var widget = new TextWidget { Label = "CPU" };
        Assert.Equal("CPU", widget.ResolveText(SensorSnapshot.Empty));
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    [InlineData(150, 1.0)]   // clamped high
    [InlineData(-20, 0.0)]   // clamped low
    public void GaugeWidget_resolves_clamped_fraction(double value, double expected)
    {
        var widget = new GaugeWidget { Sensor = SensorKeys.CpuLoad, Min = 0, Max = 100 };
        Assert.Equal(expected, widget.ResolveFraction(Snapshot((SensorKeys.CpuLoad, value))), 3);
    }

    [Fact]
    public void GaugeWidget_missing_value_is_zero_fraction()
    {
        var widget = new GaugeWidget { Sensor = SensorKeys.CpuLoad };
        Assert.Equal(0.0, widget.ResolveFraction(SensorSnapshot.Empty), 3);
    }

    [Fact]
    public void GaugeWidget_respects_non_zero_min()
    {
        var widget = new GaugeWidget { Sensor = SensorKeys.CpuTemp, Min = 30, Max = 90 };
        Assert.Equal(0.5, widget.ResolveFraction(Snapshot((SensorKeys.CpuTemp, 60.0))), 3);
    }
}
