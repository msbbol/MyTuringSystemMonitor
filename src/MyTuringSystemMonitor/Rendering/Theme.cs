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

    /// <summary>When set, the background is a vertical gradient from <see cref="Background"/> to this.</summary>
    public string? BackgroundGradientTo { get; init; }

    /// <summary>Absolute path to a full-screen background image (mathoudebine themes), or null.</summary>
    public string? BackgroundImagePath { get; init; }

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
            new TextWidget { X = 16, Y = 8, Label = "CPU", FontSize = 20, Color = "#8AB4F8" },
            new TextWidget { X = 16, Y = 30, Sensor = SensorKeys.CpuLoad, Format = "{0:F0}%", FontSize = 52 },
            new GaugeWidget { X = 16, Y = 116, Width = 288, Height = 20, Sensor = SensorKeys.CpuLoad, Color = "#8AB4F8", Background = "#1E2A38" },
            new TextWidget { X = 16, Y = 142, Sensor = SensorKeys.CpuTemp, Format = "{0:F0} °C", FontSize = 18, Color = "#9AA0A6" },

            new TextWidget { X = 16, Y = 184, Label = "GPU", FontSize = 20, Color = "#F28B82" },
            new TextWidget { X = 16, Y = 206, Sensor = SensorKeys.GpuLoad, Format = "{0:F0}%", FontSize = 52 },
            new GaugeWidget { X = 16, Y = 292, Width = 288, Height = 20, Sensor = SensorKeys.GpuLoad, Color = "#F28B82", Background = "#1E2A38" },
            new TextWidget { X = 16, Y = 318, Sensor = SensorKeys.GpuTemp, Format = "{0:F0} °C", FontSize = 18, Color = "#9AA0A6" },

            new TextWidget { X = 16, Y = 366, Label = "RAM ", Sensor = SensorKeys.RamLoad, Format = "{0:F0}%", FontSize = 24, Color = "#81C995" },
            new GaugeWidget { X = 16, Y = 404, Width = 288, Height = 20, Sensor = SensorKeys.RamLoad, Color = "#81C995", Background = "#1E2A38" },
            new TextWidget { X = 16, Y = 432, Sensor = SensorKeys.RamUsed, Format = "{0:F1} GB", FontSize = 18, Color = "#9AA0A6" },
        },
    };
}
