using System.Drawing;

namespace MyTuringSystemMonitor.Rendering;

/// <summary>The axis a theme's layout was designed for.</summary>
public enum ThemeOrientation
{
    Portrait,
    Landscape,
}

/// <summary>Helpers for working out which way round a theme is meant to be shown.</summary>
public static class ThemeOrientationHelper
{
    /// <summary>Parses "portrait"/"landscape" (case-insensitive); null for anything else.</summary>
    public static ThemeOrientation? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith("land", StringComparison.OrdinalIgnoreCase)) return ThemeOrientation.Landscape;
        if (value.StartsWith("port", StringComparison.OrdinalIgnoreCase)) return ThemeOrientation.Portrait;
        return null;
    }

    /// <summary>
    /// Infers the axis from a background image's aspect (wider than tall = landscape).
    /// Returns null when the file is missing or unreadable.
    /// </summary>
    public static ThemeOrientation? InferFromImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;
        try
        {
            using var stream = File.OpenRead(imagePath);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            if (image.Width == image.Height) return null;
            return image.Width > image.Height ? ThemeOrientation.Landscape : ThemeOrientation.Portrait;
        }
        catch
        {
            return null;
        }
    }
}
