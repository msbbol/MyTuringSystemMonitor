using System.Buffers.Binary;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using MyTuringSystemMonitor.Configuration;
using MyTuringSystemMonitor.Hardware;
using Xunit;

namespace MyTuringSystemMonitor.Tests;

public class WifiDisplayTests
{
    [Fact]
    public void FromBitmap_converts_colours_to_rgb565()
    {
        using var bmp = new Bitmap(3, 1);
        bmp.SetPixel(0, 0, Color.FromArgb(255, 0, 0));
        bmp.SetPixel(1, 0, Color.FromArgb(0, 255, 0));
        bmp.SetPixel(2, 0, Color.FromArgb(0, 0, 255));

        var pixels = Rgb565Frame.FromBitmap(bmp, 3, 1);

        Assert.Equal(new ushort[] { 0xF800, 0x07E0, 0x001F }, pixels);
    }

    [Fact]
    public void FromBitmap_centres_a_portrait_frame_on_the_square_panel()
    {
        using var bmp = new Bitmap(320, 480);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);

        var pixels = Rgb565Frame.FromBitmap(bmp, 480, 480);

        Assert.Equal(0, pixels[240 * 480 + 79]);          // left bar stays black
        Assert.Equal(0xFFFF, pixels[240 * 480 + 80]);     // theme starts 1:1 at x=80
        Assert.Equal(0xFFFF, pixels[240 * 480 + 399]);
        Assert.Equal(0, pixels[240 * 480 + 400]);         // right bar
    }

    [Fact]
    public void ChangedRegions_is_the_whole_frame_without_a_previous_one()
    {
        var regions = Rgb565Frame.ChangedRegions(null, new ushort[480 * 480], 480, 480);
        Assert.Equal([new Rectangle(0, 0, 480, 480)], regions);
    }

    [Fact]
    public void ChangedRegions_boxes_only_the_changed_pixels()
    {
        var previous = new ushort[480 * 480];
        var current = (ushort[])previous.Clone();
        current[20 * 480 + 100] = 1;
        current[22 * 480 + 130] = 1;   // same 16-row band as the first

        var regions = Rgb565Frame.ChangedRegions(previous, current, 480, 480);

        Assert.Equal([new Rectangle(100, 20, 31, 3)], regions);
        Assert.Empty(Rgb565Frame.ChangedRegions(current, current, 480, 480));
    }

    [Fact]
    public void ToWifiOptions_renders_at_theme_size_and_reverse_adds_a_half_turn()
    {
        var config = new AppConfig { Orientation = "ReversePortrait", WifiRotation = 3, WifiHost = " 192.168.1.50 " };

        var options = config.ToWifiOptions(themeIsLandscape: true);

        Assert.Equal((480, 320), (options.RenderWidth, options.RenderHeight));
        Assert.Equal(1, options.QuarterTurns);   // 3 + 2 wraps to 1
        Assert.Equal("192.168.1.50", options.Host);
    }

    [Fact]
    public void Discovery_reply_is_parsed()
    {
        var info = WifiDisplayDiscovery.TryParse("TSMW! 1 turing-esp32-a1b2 480 480", IPAddress.Parse("10.0.0.7"), 47800);

        Assert.NotNull(info);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("10.0.0.7"), 47800), info.Endpoint);
        Assert.Equal("turing-esp32-a1b2", info.Hostname);
        Assert.Null(WifiDisplayDiscovery.TryParse("hello", IPAddress.Loopback, 47800));
    }

    [Fact]
    public async Task Device_streams_a_full_frame_then_only_changes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var panel = Task.Run(() => FakePanel.Serve(listener, framesToAccept: 3));

        using (var device = new WifiDisplayDevice(new WifiDisplayOptions { Host = "127.0.0.1", Port = port, RenderWidth = 320, RenderHeight = 480, BrightnessPercent = 40 }))
        {
            device.Initialize();
            Assert.Equal((480, 480), (device.PanelWidth, device.PanelHeight));

            using var frame = new Bitmap(320, 480);
            using (var g = Graphics.FromImage(frame)) g.Clear(Color.Red);
            device.Display(frame);
            device.Display(frame);                       // unchanged: nothing but a SYNC
            frame.SetPixel(10, 10, Color.Blue);
            device.Display(frame);
        }

        var log = await panel.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(40, log.Brightness);
        Assert.True(log.ScreenOn);
        Assert.Equal([0, 1, 0, 1], log.RectsPerSync.Take(4));   // setup, full frame, no change, one pixel
        Assert.Equal(new Rectangle(0, 0, 480, 480), log.Rects[0]);
        Assert.Equal(new Rectangle(90, 10, 1, 1), log.Rects[1]);   // x shifted by the 80 px letterbox
        Assert.Equal(0x001F, log.LastPixel);
    }

    /// <summary>A minimal stand-in for the firmware: speaks the protocol and records what it got.</summary>
    private static class FakePanel
    {
        public sealed class Log
        {
            public int Brightness = -1;
            public bool ScreenOn;
            public List<Rectangle> Rects = [];
            public List<int> RectsPerSync = [];
            public ushort LastPixel;
        }

        public static Log Serve(TcpListener listener, int framesToAccept)
        {
            var log = new Log();
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            stream.Write([(byte)'T', (byte)'S', (byte)'M', (byte)'W', 1, 0xE0, 0x01, 0xE0, 0x01]);

            var rects = 0;
            var buffer = new byte[480 * 480 * 2];
            while (log.RectsPerSync.Count < framesToAccept + 1)
            {
                var op = stream.ReadByte();
                if (op < 0) break;
                switch (op)
                {
                    case 0x01:
                        var header = new byte[8];
                        stream.ReadExactly(header);
                        var rect = new Rectangle(
                            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0)), BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)),
                            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)), BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)));
                        var bytes = rect.Width * rect.Height * 2;
                        stream.ReadExactly(buffer, 0, bytes);
                        log.LastPixel = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(bytes - 2));
                        log.Rects.Add(rect);
                        rects++;
                        break;
                    case 0x02: log.Brightness = stream.ReadByte(); break;
                    case 0x03: log.ScreenOn = true; break;
                    case 0x07: stream.ReadByte(); break;
                    case 0x06:
                        log.RectsPerSync.Add(rects);
                        rects = 0;
                        stream.WriteByte(0x06);
                        break;
                    default: throw new InvalidDataException($"Unexpected opcode {op}");
                }
            }
            return log;
        }
    }
}
