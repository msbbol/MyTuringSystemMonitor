using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// A diagnostic test pattern. On a correctly-scaled screen: the white border hugs all four
/// edges, the circle is round (not an ellipse), the corner labels sit in their corners, and the
/// diagonals are straight. Distortion tells us what's wrong:
///   - circle becomes an ellipse  -> wrong aspect (width/height swapped or mis-sized)
///   - border/diagonals skew       -> wrong row stride (width mismatch)
///   - labels rotated/mirrored     -> wrong orientation
/// </summary>
public static class CalibrationImage
{
    public static Bitmap Create(int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Black);

        using var white = new Pen(Color.White, 3);
        using var cyan = new Pen(Color.Cyan, 1);
        using var red = new Pen(Color.Red, 2);
        using var textBrush = new SolidBrush(Color.White);
        using var yellow = new SolidBrush(Color.Yellow);

        // Edge border (should touch all four edges).
        g.DrawRectangle(white, 1, 1, width - 3, height - 3);

        // 40px grid so scaling is visible.
        for (var x = 40; x < width; x += 40) g.DrawLine(cyan, x, 0, x, height);
        for (var y = 40; y < height; y += 40) g.DrawLine(cyan, 0, y, width, y);

        // Diagonals (reveal shear).
        g.DrawLine(red, 0, 0, width, height);
        g.DrawLine(red, width, 0, 0, height);

        // Circle: round only if the aspect is correct.
        var d = Math.Min(width, height) - 40;
        g.DrawEllipse(new Pen(Color.Lime, 3), (width - d) / 2f, (height - d) / 2f, d, d);

        // Corner labels.
        using var font = new Font("Segoe UI", 16, FontStyle.Bold);
        g.DrawString("TL", font, yellow, 6, 6);
        g.DrawString("TR", font, yellow, width - 44, 6);
        g.DrawString("BL", font, yellow, 6, height - 30);
        g.DrawString("BR", font, yellow, width - 46, height - 30);

        // Reported size, centered.
        using var big = new Font("Segoe UI", 22, FontStyle.Bold);
        var label = $"{width} x {height}";
        var size = g.MeasureString(label, big);
        g.DrawString(label, big, textBrush, (width - size.Width) / 2f, (height - size.Height) / 2f);

        return bmp;
    }
}
