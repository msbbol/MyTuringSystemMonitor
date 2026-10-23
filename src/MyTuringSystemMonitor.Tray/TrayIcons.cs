using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MyTuringSystemMonitor.Tray;

/// <summary>
/// Draws the tray icon at runtime (a small portrait screen) tinted by state, so no .ico assets are
/// needed. Icons are cached per colour.
/// </summary>
public static class TrayIcons
{
    public static readonly Color Running = Color.FromArgb(0x35, 0xE0, 0xD0);
    public static readonly Color Busy = Color.FromArgb(0xF5, 0xA6, 0x23);
    public static readonly Color Idle = Color.FromArgb(0x83, 0x93, 0xAA);
    public static readonly Color Problem = Color.FromArgb(0xFF, 0x6B, 0x6B);

    private static readonly Dictionary<Color, Icon> Cache = new();

    public static Icon For(Color accent)
    {
        if (Cache.TryGetValue(accent, out var icon)) return icon;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var body = RoundedRect(new RectangleF(7.5f, 2.5f, 17, 27), 4);
            using var fill = new SolidBrush(Color.FromArgb(0x0F, 0x18, 0x26));
            using var outline = new Pen(accent, 2.5f);
            g.FillPath(fill, body);
            g.DrawPath(outline, body);

            using var bar = new SolidBrush(accent);
            g.FillRectangle(bar, 11, 9, 10, 4);
            g.FillRectangle(bar, 11, 16, 7, 4);
        }

        var handle = bitmap.GetHicon();
        try
        {
            icon = (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }

        Cache[accent] = icon;
        return icon;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
