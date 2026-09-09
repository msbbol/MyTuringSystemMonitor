using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// A layout: a background plus an ordered list of widgets. Loaded from <c>themes/*.json</c>
/// (see <see cref="ThemeLoader"/>) so new layouts don't require recompiling — the rough
/// equivalent of the Python project's <c>theme.yaml</c>.
/// </summary>
public sealed class Theme
{
    public string Name { get; init; } = "default";
    public string Background { get; init; } = "#000000";
    public IReadOnlyList<Widget> Widgets { get; init; } = Array.Empty<Widget>();

    /// <summary>
    /// The built-in fallback layout used when no theme file is found. Shows CPU and GPU load as
    /// big numbers with bar gauges, plus RAM — enough to prove the pipeline end to end.
    /// </summary>
    public static Theme Default { get; } = new()
    {
        Name = "default (built-in)",
        Background = "#000000",
        Widgets = new Widget[]
        {
            new TextWidget { X = 20, Y = 16, Label = "CPU", FontSize = 26, Color = "#8AB4F8" },
            new TextWidget { X = 20, Y = 52, Sensor = SensorKeys.CpuLoad, Format = "{0:F0}%", FontSize = 64 },
            new GaugeWidget { X = 20, Y = 138, Width = 280, Height = 24, Sensor = SensorKeys.CpuLoad, Color = "#8AB4F8" },
            new TextWidget { X = 20, Y = 172, Sensor = SensorKeys.CpuTemp, Format = "{0:F0} °C", FontSize = 22, Color = "#B0B0B0" },

            new TextWidget { X = 20, Y = 236, Label = "GPU", FontSize = 26, Color = "#F28B82" },
            new TextWidget { X = 20, Y = 272, Sensor = SensorKeys.GpuLoad, Format = "{0:F0}%", FontSize = 64 },
            new GaugeWidget { X = 20, Y = 358, Width = 280, Height = 24, Sensor = SensorKeys.GpuLoad, Color = "#F28B82" },
            new TextWidget { X = 20, Y = 392, Sensor = SensorKeys.GpuTemp, Format = "{0:F0} °C", FontSize = 22, Color = "#B0B0B0" },

            new TextWidget { X = 20, Y = 430, Label = "RAM ", Sensor = SensorKeys.RamLoad, Format = "{0:F0}%", FontSize = 26, Color = "#81C995" },
            new GaugeWidget { X = 20, Y = 452, Width = 280, Height = 18, Sensor = SensorKeys.RamLoad, Color = "#81C995" },
        },
    };
}
