using System.Drawing;
using System.Globalization;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// A positioned element on the screen. Value resolution (which sensor, what text) is exposed as
/// plain methods so it can be unit-tested without drawing pixels; <see cref="Render"/> does the
/// GDI+ drawing and is exercised only against real/offscreen bitmaps.
/// </summary>
public abstract class Widget
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public abstract void Render(Graphics g, SensorSnapshot snapshot);

    protected static Color ParseColor(string? html, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(html)) return fallback;
        try { return ColorTranslator.FromHtml(html); }
        catch { return fallback; }
    }
}

/// <summary>A line of text: a static label, a bound sensor value, or "label + value".</summary>
public sealed class TextWidget : Widget
{
    /// <summary>Static text. When <see cref="Sensor"/> is also set, this is used as a prefix.</summary>
    public string? Label { get; init; }

    /// <summary>Optional sensor key (see <see cref="SensorKeys"/>) to display.</summary>
    public string? Sensor { get; init; }

    /// <summary>Composite format for the value, e.g. "{0:F0}%". Invariant culture.</summary>
    public string Format { get; init; } = "{0:F0}";

    /// <summary>Shown in place of the value when the sensor is unavailable.</summary>
    public string Fallback { get; init; } = "--";

    public float FontSize { get; init; } = 24;
    public string FontFamily { get; init; } = "Segoe UI";
    public bool Bold { get; init; } = true;
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>Pure, testable: the exact string this widget would draw for a snapshot.</summary>
    public string ResolveText(SensorSnapshot snapshot)
    {
        if (Sensor is null)
            return Label ?? string.Empty;

        var value = snapshot.Get(Sensor);
        var valueText = value.HasValue
            ? string.Format(CultureInfo.InvariantCulture, Format, value.Value)
            : Fallback;

        return Label is null ? valueText : Label + valueText;
    }

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        var text = ResolveText(snapshot);
        if (text.Length == 0) return;

        using var font = new Font(FontFamily, FontSize, Bold ? FontStyle.Bold : FontStyle.Regular);
        using var brush = new SolidBrush(ParseColor(Color, System.Drawing.Color.White));
        g.DrawString(text, font, brush, new PointF(X, Y));
    }
}

/// <summary>A horizontal bar gauge for a numeric sensor scaled between Min and Max.</summary>
public sealed class GaugeWidget : Widget
{
    public string Sensor { get; init; } = string.Empty;
    public double Min { get; init; }
    public double Max { get; init; } = 100;

    public string Color { get; init; } = "#00A0FF";       // fill
    public string Background { get; init; } = "#203040";   // track

    public double? ResolveValue(SensorSnapshot snapshot) => snapshot.Get(Sensor);

    /// <summary>Pure, testable: fill fraction in [0,1] (0 when the value is missing).</summary>
    public double ResolveFraction(SensorSnapshot snapshot)
    {
        var value = ResolveValue(snapshot);
        if (!value.HasValue || Max <= Min) return 0;
        return Math.Clamp((value.Value - Min) / (Max - Min), 0, 1);
    }

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        var w = Width > 0 ? Width : 100;
        var h = Height > 0 ? Height : 20;

        using var track = new SolidBrush(ParseColor(Background, System.Drawing.Color.FromArgb(32, 48, 64)));
        using var fill = new SolidBrush(ParseColor(Color, System.Drawing.Color.DeepSkyBlue));

        g.FillRectangle(track, X, Y, w, h);
        var fillWidth = (int)Math.Round(w * ResolveFraction(snapshot));
        if (fillWidth > 0)
            g.FillRectangle(fill, X, Y, fillWidth, h);
    }
}
