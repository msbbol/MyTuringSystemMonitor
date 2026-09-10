using System.Drawing;
using System.Drawing.Text;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>
/// Loads and caches TrueType fonts from files (mathoudebine themes ship their own .ttf fonts).
/// Returns null when a file is missing so callers can fall back to a system font.
/// </summary>
public static class FontCache
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<string, FontFamily?> ByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static FontFamily? FromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        lock (Gate)
        {
            if (ByPath.TryGetValue(path, out var cached))
                return cached;

            FontFamily? family = null;
            try
            {
                if (File.Exists(path))
                {
                    var before = Collection.Families.Length;
                    Collection.AddFontFile(path);
                    var families = Collection.Families;
                    if (families.Length > before)
                        family = families[^1];
                }
            }
            catch
            {
                family = null;
            }

            ByPath[path] = family;
            return family;
        }
    }
}
