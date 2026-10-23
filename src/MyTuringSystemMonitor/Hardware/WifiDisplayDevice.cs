using System.Buffers.Binary;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>
/// An ESP32 panel on the network (Guition ESP32-4848S040, 480x480) running the firmware in
/// <c>firmware/esp32-4848s040</c>. Frames are converted to RGB565 and only the rectangles that
/// changed since the last frame are sent over TCP; each frame ends with a SYNC the panel answers,
/// so a panel that vanished is noticed within <see cref="WifiDisplayOptions.ResponseTimeout"/>.
/// </summary>
public sealed class WifiDisplayDevice : IDisplayDevice
{
    public const string Auto = "AUTO";
    public const int DefaultPort = 47800;
    public const int DefaultDiscoveryPort = 47801;
    public const int ProtocolVersion = 1;

    private const byte CmdRect = 0x01;
    private const byte CmdBrightness = 0x02;
    private const byte CmdScreenOn = 0x03;
    private const byte CmdScreenOff = 0x04;
    private const byte CmdFill = 0x05;
    private const byte CmdSync = 0x06;
    private const byte CmdRotation = 0x07;

    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(1.5);

    private readonly WifiDisplayOptions _options;
    private readonly Action<string>? _log;
    private TcpClient? _tcp;
    private NetworkStream? _socket;
    private BufferedStream? _out;
    private ushort[]? _previous;
    private IPEndPoint? _lastEndpoint;

    public WifiDisplayDevice(WifiDisplayOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log;
    }

    /// <summary>The render size; the panel itself may be bigger (see <see cref="PanelWidth"/>).</summary>
    public int Width => _options.RenderWidth;
    public int Height => _options.RenderHeight;

    /// <summary>Native panel size as reported by the firmware (0 until connected).</summary>
    public int PanelWidth { get; private set; }
    public int PanelHeight { get; private set; }

    /// <summary>"ip:port" of the panel in use, or null when not connected.</summary>
    public string? Endpoint => _tcp is null ? null : _lastEndpoint?.ToString();

    public void Initialize() => Open();

    public bool Reconnect()
    {
        CloseQuietly();
        try
        {
            Open();
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Reconnect to the Wi-Fi display failed: {ex.Message}");
            CloseQuietly();
            return false;
        }
    }

    private void Open()
    {
        var endpoint = ResolveEndpoint();
        var tcp = new TcpClient(endpoint.AddressFamily) { NoDelay = true };
        try
        {
            using (var cts = new CancellationTokenSource(_options.ConnectTimeout))
            {
                try { tcp.ConnectAsync(endpoint, cts.Token).AsTask().GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { throw new TimeoutException($"No answer from {endpoint} within {_options.ConnectTimeout.TotalSeconds:F0} s."); }
            }

            var timeoutMs = (int)_options.ResponseTimeout.TotalMilliseconds;
            tcp.ReceiveTimeout = timeoutMs;
            tcp.SendTimeout = timeoutMs;
            var socket = tcp.GetStream();

            var hello = new byte[9];
            socket.ReadExactly(hello);
            if (hello[0] != 'T' || hello[1] != 'S' || hello[2] != 'M' || hello[3] != 'W')
                throw new InvalidDataException($"{endpoint} isn't a Turing Wi-Fi display.");
            if (hello[4] != ProtocolVersion)
                throw new InvalidDataException($"{endpoint} speaks protocol v{hello[4]}, expected v{ProtocolVersion} — update the firmware.");

            PanelWidth = BinaryPrimitives.ReadUInt16LittleEndian(hello.AsSpan(5));
            PanelHeight = BinaryPrimitives.ReadUInt16LittleEndian(hello.AsSpan(7));

            _tcp = tcp;
            _socket = socket;
            _out = new BufferedStream(socket, 64 * 1024);
            _lastEndpoint = endpoint;
            _previous = null;   // the panel may show anything (status screen) — send a full frame

            _out.WriteByte(CmdRotation);
            _out.WriteByte((byte)(((_options.QuarterTurns % 4) + 4) % 4));
            _out.WriteByte(CmdBrightness);
            _out.WriteByte((byte)Math.Clamp(_options.BrightnessPercent, 0, 100));
            _out.WriteByte(CmdScreenOn);
            Sync();

            _log?.Invoke($"Wi-Fi display at {endpoint}: panel {PanelWidth}x{PanelHeight}, rendering at {Width}x{Height}, " +
                         $"rotation {_options.QuarterTurns * 90}°.");
        }
        catch
        {
            _tcp = null;
            _socket = null;
            _out = null;
            tcp.Dispose();
            throw;
        }
    }

    private IPEndPoint ResolveEndpoint()
    {
        var host = _options.Host.Trim();
        if (!host.Equals(Auto, StringComparison.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(host, out var ip))
                return new IPEndPoint(ip, _options.Port);

            var addresses = Dns.GetHostAddresses(host);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault()
                          ?? throw new InvalidOperationException($"Can't resolve {host}.");
            return new IPEndPoint(address, _options.Port);
        }

        var found = WifiDisplayDiscovery.Find(_options.DiscoveryPort, _options.Port, DiscoveryTimeout);
        if (found.Count > 0)
        {
            if (found.Count > 1)
                _log?.Invoke($"Found {found.Count} Wi-Fi displays ({string.Join(", ", found.Select(f => $"{f.Hostname} {f.Endpoint.Address}"))}); " +
                             $"using {found[0].Hostname}. Set the address explicitly to pick another.");
            else if (_lastEndpoint is null || !_lastEndpoint.Equals(found[0].Endpoint))
                _log?.Invoke($"Found Wi-Fi display {found[0].Hostname} at {found[0].Endpoint.Address}.");
            return found[0].Endpoint;
        }

        // Broadcasts can be dropped (busy Wi-Fi, firewall); the last address is worth a try.
        return _lastEndpoint
               ?? throw new InvalidOperationException("No Wi-Fi display answered on the local network. " +
                                                      "Check that it shows \"Waiting for PC\" and is on the same network, or set its address.");
    }

    public void Display(Bitmap frame)
    {
        var output = _out ?? throw new InvalidOperationException("Initialize() must be called before Display().");

        var current = Rgb565Frame.FromBitmap(frame, PanelWidth, PanelHeight);
        foreach (var rect in Rgb565Frame.ChangedRegions(_previous, current, PanelWidth, PanelHeight))
            WriteRect(output, current, rect);

        // Sent even when nothing changed: it doubles as the keep-alive the panel expects.
        Sync();
        _previous = current;
    }

    private void WriteRect(Stream output, ushort[] pixels, Rectangle rect)
    {
        Span<byte> header = stackalloc byte[9];
        header[0] = CmdRect;
        BinaryPrimitives.WriteUInt16LittleEndian(header[1..], (ushort)rect.X);
        BinaryPrimitives.WriteUInt16LittleEndian(header[3..], (ushort)rect.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(header[5..], (ushort)rect.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[7..], (ushort)rect.Height);
        output.Write(header);

        // Windows is little-endian, which is the wire order, so rows go out as-is.
        for (var y = rect.Top; y < rect.Bottom; y++)
            output.Write(MemoryMarshal.AsBytes(pixels.AsSpan(y * PanelWidth + rect.X, rect.Width)));
    }

    /// <summary>Flushes everything queued and waits until the panel confirms it has drawn it.</summary>
    private void Sync()
    {
        var output = _out ?? throw new InvalidOperationException("Not connected.");
        output.WriteByte(CmdSync);
        output.Flush();

        var ack = _socket!.ReadByte();
        if (ack < 0) throw new IOException("The Wi-Fi display closed the connection.");
        if (ack != CmdSync) throw new InvalidDataException($"Unexpected reply 0x{ack:X2} from the Wi-Fi display.");
    }

    public void Clear()
    {
        var output = _out;
        if (output is null) return;
        output.WriteByte(CmdFill);
        output.WriteByte(0);
        output.WriteByte(0);
        Sync();
        _previous = new ushort[PanelWidth * PanelHeight];   // the panel is all black now
    }

    public void TurnOff()
    {
        var output = _out;
        if (output is null) return;
        output.WriteByte(CmdScreenOff);
        Sync();
    }

    /// <summary>Releases the socket even if the panel has already vanished.</summary>
    private void CloseQuietly()
    {
        var tcp = _tcp;
        _tcp = null;
        _socket = null;
        _out = null;
        if (tcp is null) return;
        try { tcp.Dispose(); } catch { /* connection already gone */ }
    }

    public void Dispose() => CloseQuietly();
}
