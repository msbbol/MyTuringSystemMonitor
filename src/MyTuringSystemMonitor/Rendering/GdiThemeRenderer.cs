using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>Renders a theme to a GDI+ <see cref="Bitmap"/>. Windows-only (System.Drawing).</summary>
public sealed class GdiThemeRenderer : IThemeRenderer
{
    /// <summary>
    /// GDI+ objects — notably the cached private fonts shared by all themes — aren't thread-safe,
    /// and the service renders theme previews for the web UI while the monitor loop is drawing.
    /// Frames are small (a few ms), so simply serialise all rendering in the process.
    /// </summary>
    private static readonly object RenderGate = new();

    public Bitmap Render(Theme theme, SensorSnapshot snapshot, int width, int height)
    {
        lock (RenderGate)
            return RenderCore(theme, snapshot, width, height);
    }

    private static Bitmap RenderCore(Theme theme, SensorSnapshot snapshot, int width, int height)
    {
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height));
        try
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(ParseBackground(theme.Background));

            if (!string.IsNullOrWhiteSpace(theme.BackgroundGradientTo))
            {
                using var brush = new LinearGradientBrush(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ParseBackground(theme.Background),
                    ParseBackground(theme.BackgroundGradientTo),
                    LinearGradientMode.Vertical);
                g.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
            }

            if (!string.IsNullOrWhiteSpace(theme.BackgroundImagePath) && File.Exists(theme.BackgroundImagePath))
            {
                try
                {
                    using var bg = Image.FromFile(theme.BackgroundImagePath);
                    g.DrawImage(bg, 0, 0, bitmap.Width, bitmap.Height);
                }
                catch { /* fall back to the solid background already painted */ }
            }

            foreach (var widget in theme.Widgets)
                widget.Render(g, snapshot);

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static Color ParseBackground(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return Color.Black;
        try { return ColorTranslator.FromHtml(html); }
        catch { return Color.Black; }
    }
}
