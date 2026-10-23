using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>
/// Pixel work for <see cref="WifiDisplayDevice"/>, kept free of sockets so it can be unit-tested:
/// fitting a rendered frame onto the panel, converting it to RGB565, and finding what changed
/// since the previous frame so only those rectangles go over Wi-Fi.
/// </summary>
public static class Rgb565Frame
{
    /// <summary>Rows per comparison band; each band yields at most one dirty rectangle.</summary>
    public const int BandHeight = 16;

    /// <summary>
    /// Converts <paramref name="frame"/> to a <paramref name="width"/> x <paramref name="height"/>
    /// RGB565 buffer (row-major). A frame of another size is scaled to fit, keeping its aspect
    /// ratio, and centred on black — a 320x480 theme lands 1:1 in the middle of a 480x480 panel.
    /// </summary>
    public static ushort[] FromBitmap(Bitmap frame, int width, int height)
    {
        if (frame.Width == width && frame.Height == height)
            return Convert(frame);

        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Black);
            var scale = Math.Min((double)width / frame.Width, (double)height / frame.Height);
            var w = (int)Math.Round(frame.Width * scale);
            var h = (int)Math.Round(frame.Height * scale);
            var x = (width - w) / 2;
            var y = (height - h) / 2;

            if (w == frame.Width && h == frame.Height)
            {
                g.DrawImageUnscaled(frame, x, y);
            }
            else
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(frame, new Rectangle(x, y, w, h));
            }
        }
        return Convert(canvas);
    }

    private static ushort[] Convert(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var result = new ushort[width * height];
            var row = new int[width];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, width);
                var offset = y * width;
                for (var x = 0; x < width; x++)
                {
                    var argb = row[x];
                    result[offset + x] = (ushort)(((argb >> 8) & 0xF800) | ((argb >> 5) & 0x07E0) | ((argb >> 3) & 0x001F));
                }
            }
            return result;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// The rectangles of <paramref name="current"/> that differ from <paramref name="previous"/>:
    /// the frame is cut into horizontal bands and each band contributes the tight box around its
    /// changed pixels. With no previous frame the whole frame is one rectangle.
    /// </summary>
    public static List<Rectangle> ChangedRegions(ushort[]? previous, ushort[] current, int width, int height)
    {
        var regions = new List<Rectangle>();
        if (previous is null || previous.Length != current.Length)
        {
            regions.Add(new Rectangle(0, 0, width, height));
            return regions;
        }

        for (var bandTop = 0; bandTop < height; bandTop += BandHeight)
        {
            var bandBottom = Math.Min(bandTop + BandHeight, height);
            int minX = width, maxX = -1, minY = -1, maxY = -1;

            for (var y = bandTop; y < bandBottom; y++)
            {
                var offset = y * width;
                var prevRow = previous.AsSpan(offset, width);
                var curRow = current.AsSpan(offset, width);
                if (prevRow.SequenceEqual(curRow))
                    continue;

                var left = 0;
                while (prevRow[left] == curRow[left]) left++;
                var right = width - 1;
                while (prevRow[right] == curRow[right]) right--;

                minX = Math.Min(minX, left);
                maxX = Math.Max(maxX, right);
                if (minY < 0) minY = y;
                maxY = y;
            }

            if (maxX >= 0)
                regions.Add(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1));
        }
        return regions;
    }
}
