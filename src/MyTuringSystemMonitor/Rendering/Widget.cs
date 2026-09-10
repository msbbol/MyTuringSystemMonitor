using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json.Serialization;
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

    /// <summary>Optional numeric sensor key (see <see cref="SensorKeys"/>) to display.</summary>
    public string? Sensor { get; init; }

    /// <summary>Optional text sensor key (e.g. the busiest process name).</summary>
    public string? TextSensor { get; init; }

    /// <summary>When "bytes", format the numeric value as a human-readable size (7.4M, 860K).</summary>
    public string Humanize { get; init; } = string.Empty;

    /// <summary>Composite format for the value, e.g. "{0:F0}%". Invariant culture.</summary>
    public string Format { get; init; } = "{0:F0}";

    /// <summary>Multiplier applied to the raw sensor value before formatting (e.g. MHz→GHz = 0.001).</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>Appended after the formatted value when set (mathoudebine SHOW_UNIT).</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Shown in place of the value when the sensor is unavailable.</summary>
    public string Fallback { get; init; } = "--";

    public float FontSize { get; init; } = 24;
    public string FontFamily { get; init; } = "Segoe UI";

    /// <summary>Absolute path to a .ttf; when set it wins over <see cref="FontFamily"/>.</summary>
    public string? FontFile { get; init; }

    /// <summary>True = FontSize is pixels (mathoudebine/PIL), false = points (JSON themes).</summary>
    public bool PixelFontSize { get; init; }

    public bool Bold { get; init; } = true;
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>PIL-style two-letter anchor (e.g. "lm"); null = top-left.</summary>
    public string? Anchor { get; init; }

    /// <summary>Pure, testable: the exact string this widget would draw for a snapshot.</summary>
    public string ResolveText(SensorSnapshot snapshot)
    {
        if (Sensor is null && TextSensor is null)
            return Label ?? string.Empty;

        string valueText;
        if (Sensor is not null)
        {
            var value = snapshot.Get(Sensor);
            if (value.HasValue)
            {
                valueText = Humanize == "bytes"
                    ? HumanBytes(value.Value * Scale)
                    : string.Format(CultureInfo.InvariantCulture, Format, value.Value * Scale);
                if (Unit.Length > 0) valueText += Unit;
            }
            else
            {
                valueText = Fallback;
            }
        }
        else
        {
            valueText = snapshot.GetText(TextSensor!) ?? Fallback;
        }

        return Label is null ? valueText : Label + valueText;
    }

    private static string HumanBytes(double b)
    {
        var c = CultureInfo.InvariantCulture;
        if (b >= 1_000_000_000) return (b / 1_000_000_000).ToString("F1", c) + "G";
        if (b >= 1_000_000) return (b / 1_000_000).ToString("F1", c) + "M";
        if (b >= 1_000) return (b / 1_000).ToString("F0", c) + "K";
        return b.ToString("F0", c);
    }

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        TextDraw.Draw(g, ResolveText(snapshot), X, Y, FontFile, FontFamily, FontSize, PixelFontSize, Bold, Color, Anchor);
    }
}

/// <summary>Draws the current date/time using a .NET format string.</summary>
public sealed class DateTimeWidget : Widget
{
    public string Format { get; init; } = "HH:mm:ss";
    public float FontSize { get; init; } = 18;
    public string FontFamily { get; init; } = "Segoe UI";
    public string? FontFile { get; init; }
    public bool PixelFontSize { get; init; }
    public bool Bold { get; init; } = true;
    public string Color { get; init; } = "#FFFFFF";
    public string? Anchor { get; init; }

    /// <summary>Injectable clock for deterministic tests; defaults to <see cref="DateTime.Now"/>.</summary>
    [JsonIgnore]
    public Func<DateTime>? Clock { get; init; }

    public string ResolveText() =>
        (Clock?.Invoke() ?? DateTime.Now).ToString(Format, CultureInfo.InvariantCulture);

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        TextDraw.Draw(g, ResolveText(), X, Y, FontFile, FontFamily, FontSize, PixelFontSize, Bold, Color, Anchor);
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
    public int CornerRadius { get; init; }                 // 0 = square ends

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

        var fillWidth = (int)Math.Round(w * ResolveFraction(snapshot));

        if (CornerRadius > 0)
        {
            using (var trackPath = PanelWidget.RoundedRect(X, Y, w, h, CornerRadius))
                g.FillPath(track, trackPath);
            if (fillWidth > 0)
                using (var fillPath = PanelWidget.RoundedRect(X, Y, Math.Max(fillWidth, CornerRadius * 2), h, CornerRadius))
                    g.FillPath(fill, fillPath);
        }
        else
        {
            g.FillRectangle(track, X, Y, w, h);
            if (fillWidth > 0)
                g.FillRectangle(fill, X, Y, fillWidth, h);
        }
    }
}

/// <summary>A circular (radial) gauge — the mathoudebine RADIAL element. A full disc when the ring
/// width covers the radius, otherwise a ring.</summary>
public sealed class RadialWidget : Widget
{
    public string Sensor { get; init; } = string.Empty;
    public double Min { get; init; }
    public double Max { get; init; } = 100;

    /// <summary>Circle centre (mathoudebine RADIAL X/Y are the centre) and geometry.</summary>
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    public int Radius { get; init; } = 50;
    public int Thickness { get; init; } = 10;

    public double AngleStart { get; init; } = 270;
    public double AngleEnd { get; init; } = 270;
    public bool Clockwise { get; init; } = true;

    public string BarColor { get; init; } = "#FFFFFF";
    public string TrackColor { get; init; } = "#000000";
    public bool DrawTrack { get; init; } = true;
    public bool RoundCap { get; init; }

    public double ResolveFraction(SensorSnapshot snapshot)
    {
        var value = snapshot.Get(Sensor);
        if (!value.HasValue || Max <= Min) return 0;
        return Math.Clamp((value.Value - Min) / (Max - Min), 0, 1);
    }

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        var total = Math.Abs(AngleStart - AngleEnd) < 0.5 ? 360.0 : SpanDegrees();
        var sign = Clockwise ? 1f : -1f;
        var valueSweep = (float)(total * ResolveFraction(snapshot));
        var start = (float)AngleStart;

        var barColor = ParseColor(BarColor, System.Drawing.Color.White);
        var trackColor = ParseColor(TrackColor, System.Drawing.Color.Black);

        if (Thickness >= Radius)
        {
            // Filled pie.
            var rect = new RectangleF(CenterX - Radius, CenterY - Radius, Radius * 2, Radius * 2);
            using var trackBrush = new SolidBrush(trackColor);
            using var barBrush = new SolidBrush(barColor);
            if (DrawTrack) g.FillPie(trackBrush, rect.X, rect.Y, rect.Width, rect.Height, start, sign * (float)total);
            if (valueSweep > 0) g.FillPie(barBrush, rect.X, rect.Y, rect.Width, rect.Height, start, sign * valueSweep);
        }
        else
        {
            // Ring: draw an arc with a thick pen along the mid-radius circle.
            var mid = Radius - Thickness / 2f;
            var rect = new RectangleF(CenterX - mid, CenterY - mid, mid * 2, mid * 2);
            var cap = RoundCap ? LineCap.Round : LineCap.Flat;
            using var trackPen = new Pen(trackColor, Thickness) { StartCap = cap, EndCap = cap };
            using var barPen = new Pen(barColor, Thickness) { StartCap = cap, EndCap = cap };
            if (DrawTrack) g.DrawArc(trackPen, rect, start, sign * (float)total);
            if (valueSweep > 0) g.DrawArc(barPen, rect, start, sign * valueSweep);
        }
    }

    private double SpanDegrees()
    {
        var diff = (Clockwise ? AngleEnd - AngleStart : AngleStart - AngleEnd) % 360;
        if (diff <= 0) diff += 360;
        return diff;
    }
}

/// <summary>A rolling sparkline (line or bars) of a sensor's recent values. Holds its own history,
/// so it fills in over time as the loop runs (a single preview frame shows just the latest point).</summary>
public sealed class SparklineWidget : Widget
{
    public string Sensor { get; init; } = string.Empty;
    public double Min { get; init; }
    public double Max { get; init; } = 100;  // Max <= Min => auto-scale to the buffer
    public string Color { get; init; } = "#35E0D0";
    public float Thickness { get; init; } = 2;
    public int MaxSamples { get; init; } = 48;
    public string Style { get; init; } = "line"; // or "bars"

    [JsonIgnore]
    private readonly Queue<double> _history = new();

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        var value = snapshot.Get(Sensor) ?? 0;
        _history.Enqueue(value);
        while (_history.Count > Math.Max(2, MaxSamples)) _history.Dequeue();

        var w = Width > 0 ? Width : 100;
        var h = Height > 0 ? Height : 30;
        var samples = _history.ToArray();

        double min = Min, max = Max;
        if (max <= min)
        {
            min = 0;
            max = Math.Max(1.0, samples.DefaultIfEmpty(1).Max());
        }

        float Y(double v) => (float)(Y0 + h - Math.Clamp((v - min) / (max - min), 0, 1) * h);
        var color = ParseColor(Color, System.Drawing.Color.DeepSkyBlue);

        if (string.Equals(Style, "bars", StringComparison.OrdinalIgnoreCase))
        {
            var n = samples.Length;
            var slot = w / (float)Math.Max(1, n);
            var barW = Math.Max(1f, slot * 0.6f);
            using var brush = new SolidBrush(color);
            for (var i = 0; i < n; i++)
            {
                var bx = X + i * slot + (slot - barW) / 2f;
                var by = Y(samples[i]);
                g.FillRectangle(brush, bx, by, barW, Y0 + h - by);
            }
        }
        else
        {
            if (samples.Length < 2) return;
            var pts = new PointF[samples.Length];
            for (var i = 0; i < samples.Length; i++)
                pts[i] = new PointF(X + i / (float)(samples.Length - 1) * w, Y(samples[i]));
            using var pen = new Pen(color, Thickness) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, pts);
        }
    }

    private int Y0 => Y;
}

/// <summary>A rounded rectangle panel — the card chrome used by the custom themes.</summary>
public sealed class PanelWidget : Widget
{
    public string Fill { get; init; } = "#141821";
    public string? Stroke { get; init; }
    public float StrokeWidth { get; init; } = 1;
    public int CornerRadius { get; init; } = 12;

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        var w = Width > 0 ? Width : 100;
        var h = Height > 0 ? Height : 100;

        using var path = RoundedRect(X, Y, w, h, CornerRadius);
        using var fill = new SolidBrush(ParseColor(Fill, System.Drawing.Color.FromArgb(20, 24, 33)));
        g.FillPath(fill, path);

        if (!string.IsNullOrWhiteSpace(Stroke))
        {
            using var pen = new Pen(ParseColor(Stroke, System.Drawing.Color.Gray), StrokeWidth);
            g.DrawPath(pen, path);
        }
    }

    internal static GraphicsPath RoundedRect(int x, int y, int w, int h, int radius)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        var path = new GraphicsPath();
        if (r == 0)
        {
            path.AddRectangle(new Rectangle(x, y, w, h));
            return path;
        }
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>An analogue clock face with hour/minute/second hands driven by the current time.</summary>
public sealed class AnalogClockWidget : Widget
{
    public int CenterX { get; init; }
    public int CenterY { get; init; }
    public int Radius { get; init; } = 60;

    public string? FaceColor { get; init; }
    public string? FaceStroke { get; init; }
    public string TickColor { get; init; } = "#F5A623";
    public string HandColor { get; init; } = "#FFFFFF";
    public string SecondColor { get; init; } = "#F5A623";
    public string HubColor { get; init; } = "#F5A623";

    /// <summary>Injectable clock for deterministic tests; defaults to <see cref="DateTime.Now"/>.</summary>
    [JsonIgnore]
    public Func<DateTime>? Clock { get; init; }

    public override void Render(Graphics g, SensorSnapshot snapshot)
    {
        var now = Clock?.Invoke() ?? DateTime.Now;
        float cx = CenterX, cy = CenterY, r = Radius;

        if (!string.IsNullOrWhiteSpace(FaceColor))
        {
            using var face = new SolidBrush(ParseColor(FaceColor, System.Drawing.Color.FromArgb(40, 52, 64)));
            g.FillEllipse(face, cx - r, cy - r, r * 2, r * 2);
        }
        if (!string.IsNullOrWhiteSpace(FaceStroke))
        {
            using var pen = new Pen(ParseColor(FaceStroke, System.Drawing.Color.Gray), 2);
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }

        // Hour ticks.
        using (var tickPen = new Pen(ParseColor(TickColor, System.Drawing.Color.Orange), 2))
            for (var i = 0; i < 12; i++)
            {
                var a = i * 30;
                var (ox, oy) = Point(cx, cy, a, r * 0.92f);
                var (ix, iy) = Point(cx, cy, a, r * 0.78f);
                g.DrawLine(tickPen, ox, oy, ix, iy);
            }

        var hourAngle = ((now.Hour % 12) + now.Minute / 60.0) * 30;
        var minuteAngle = (now.Minute + now.Second / 60.0) * 6;
        var secondAngle = (now.Second + now.Millisecond / 1000.0) * 6;

        DrawHand(g, cx, cy, hourAngle, r * 0.5f, 4, ParseColor(HandColor, System.Drawing.Color.White));
        DrawHand(g, cx, cy, minuteAngle, r * 0.72f, 3, ParseColor(HandColor, System.Drawing.Color.White));
        DrawHand(g, cx, cy, secondAngle, r * 0.82f, 1.5f, ParseColor(SecondColor, System.Drawing.Color.Orange));

        var hub = 4f;
        using var hubBrush = new SolidBrush(ParseColor(HubColor, System.Drawing.Color.Orange));
        g.FillEllipse(hubBrush, cx - hub, cy - hub, hub * 2, hub * 2);
    }

    private static void DrawHand(Graphics g, float cx, float cy, double angleDeg, float length, float width, Color color)
    {
        var (x, y) = Point(cx, cy, angleDeg, length);
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, cx, cy, x, y);
    }

    private static (float x, float y) Point(float cx, float cy, double angleDeg, float distance)
    {
        var theta = angleDeg * Math.PI / 180.0; // 0 = 12 o'clock, clockwise
        return ((float)(cx + distance * Math.Sin(theta)), (float)(cy - distance * Math.Cos(theta)));
    }
}
