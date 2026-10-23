using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MyTuringSystemMonitor.Hardware;

/// <summary>An ESP32 Wi-Fi panel that answered a discovery broadcast.</summary>
public sealed record WifiDisplayInfo(IPEndPoint Endpoint, string Hostname, int Width, int Height);

/// <summary>
/// Finds ESP32 Wi-Fi panels on the LAN: broadcasts <c>"TSMW?"</c> to the discovery port and
/// collects <c>"TSMW! &lt;version&gt; &lt;hostname&gt; &lt;w&gt; &lt;h&gt;"</c> replies. The firmware
/// is in <c>firmware/esp32-4848s040</c>.
/// </summary>
public static class WifiDisplayDiscovery
{
    private static readonly byte[] Request = "TSMW?"u8.ToArray();

    public static List<WifiDisplayInfo> Find(int discoveryPort, int tcpPort, TimeSpan timeout)
    {
        var found = new List<WifiDisplayInfo>();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };

        // An ICMP "port unreachable" for any target (e.g. a VPN adapter's own address) would
        // otherwise make the next Receive() fail with ConnectionReset and end the search early.
        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        try { udp.Client.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null); }
        catch (SocketException) { /* not supported — the ConnectionReset check below still copes */ }

        // 255.255.255.255 only leaves through one adapter on Windows, so also hit each adapter's
        // own subnet broadcast address (Ethernet + Wi-Fi + VPN machines are common).
        foreach (var target in BroadcastAddresses())
        {
            try { udp.Send(Request, Request.Length, new IPEndPoint(target, discoveryPort)); }
            catch (SocketException) { /* adapter went away or can't broadcast — try the others */ }
        }

        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) break;
            udp.Client.ReceiveTimeout = Math.Max(1, (int)left.TotalMilliseconds);

            IPEndPoint? from = null;
            byte[] reply;
            try { reply = udp.Receive(ref from); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { continue; }
            catch (SocketException) { break; }   // timed out

            if (TryParse(Encoding.ASCII.GetString(reply), from.Address, tcpPort) is { } info &&
                !found.Exists(f => f.Endpoint.Equals(info.Endpoint)))
                found.Add(info);
        }
        return found;
    }

    /// <summary>Parses a discovery reply; null if it isn't one.</summary>
    public static WifiDisplayInfo? TryParse(string reply, IPAddress from, int tcpPort)
    {
        var parts = reply.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || parts[0] != "TSMW!") return null;
        if (!int.TryParse(parts[3], out var w) || !int.TryParse(parts[4], out var h)) return null;
        return new WifiDisplayInfo(new IPEndPoint(from, tcpPort), parts[2], w, h);
    }

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;

        NetworkInterface[] adapters;
        try { adapters = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { yield break; }

        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                    continue;
                var mask = unicast.IPv4Mask.GetAddressBytes();
                if (mask.All(b => b == 0xFF))
                    continue;   // point-to-point link (VPN): no subnet to broadcast to
                var ip = unicast.Address.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                    broadcast[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(broadcast);
            }
        }
    }
}
