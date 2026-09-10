using System.Drawing;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// Shared text drawing used by <see cref="TextWidget"/> and <see cref="DateTimeWidget"/>.
/// Handles file-based fonts, pixel vs point sizing, and PIL-style anchors so mathoudebine
/// themes (which position text with a two-letter anchor like "lm") line up.
/// </summary>
public static class TextDraw
{
    public static void Draw(
        Graphics g,
        string text,
        float x,
        float y,
        string? fontFile,
        string fontFamily,
        float fontSize,
        bool pixelFontSize,
        bool bold,
        string colorHtml,
        string? anchor)
    {
        if (string.IsNullOrEmpty(text)) return;

        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        var unit = pixelFontSize ? GraphicsUnit.Pixel : GraphicsUnit.Point;

        var family = FontCache.FromFile(fontFile);
        var ownFamily = false;
        if (family is null)
        {
            try { family = new FontFamily(fontFamily); ownFamily = true; }
            catch { family = FontFamily.GenericSansSerif; }
        }

        // Some faces don't carry a bold variant; fall back to whatever style is available.
        if (!family.IsStyleAvailable(style))
            style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;

        Font? font = null;
        try
        {
            font = new Font(family, fontSize, style, unit);
            using var brush = new SolidBrush(ParseColor(colorHtml));
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

            var (dx, dy) = AnchorOffset(anchor, g, text, font, format);
            g.DrawString(text, font, brush, x + dx, y + dy, format);
        }
        finally
        {
            font?.Dispose();
            if (ownFamily) family.Dispose();
        }
    }

    private static (float dx, float dy) AnchorOffset(string? anchor, Graphics g, string text, Font font, StringFormat format)
    {
        // Default (no anchor) = top-left, matching PIL's default "la".
        if (string.IsNullOrEmpty(anchor) || anchor.Length < 2)
            return (0, 0);

        var size = g.MeasureString(text, font, int.MaxValue, format);

        var dx = anchor[0] switch
        {
            'm' => -size.Width / 2f,
            'r' => -size.Width,
            _ => 0f,       // 'l'
        };
        var dy = anchor[1] switch
        {
            'm' => -size.Height / 2f,
            's' or 'd' or 'b' => -size.Height,
            _ => 0f,       // 'a' (ascender/top)
        };
        return (dx, dy);
    }

    private static Color ParseColor(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return Color.White;
        try { return ColorTranslator.FromHtml(html); }
        catch { return Color.White; }
    }
}
