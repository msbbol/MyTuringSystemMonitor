using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MyTuringSystemMonitor.Sensors;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>Renders a theme to a GDI+ <see cref="Bitmap"/>. Windows-only (System.Drawing).</summary>
public sealed class GdiThemeRenderer : IThemeRenderer
{
    public Bitmap Render(Theme theme, SensorSnapshot snapshot, int width, int height)
    {
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height));
        try
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(ParseBackground(theme.Background));

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
