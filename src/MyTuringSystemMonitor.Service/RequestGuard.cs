namespace MyTuringSystemMonitor.Service;

/// <summary>
/// Keeps the control API local. Kestrel only listens on loopback, but a web page open in the user's
/// browser could still reach it, so:
/// <list type="bullet">
///   <item>the Host header must be a loopback name — defeats DNS-rebinding attacks;</item>
///   <item>state-changing requests must carry <see cref="HeaderName"/> — a cross-site page can't add
///   a custom header without a CORS pre-flight, which this server never approves (defeats CSRF).</item>
/// </list>
/// </summary>
public static class RequestGuard
{
    public const string HeaderName = "X-Turing-Monitor";

    public static bool IsAllowed(string? hostHeader, string method, bool hasControlHeader)
    {
        if (!IsLoopbackHost(hostHeader)) return false;

        var readOnly = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        return readOnly || hasControlHeader;
    }

    public static bool IsLoopbackHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;

        var host = hostHeader.Trim();
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']');
            if (end < 0) return false;
            host = host[..(end + 1)];
        }
        else
        {
            var colon = host.IndexOf(':');
            if (colon >= 0) host = host[..colon];
        }

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host == "127.0.0.1"
               || host == "[::1]";
    }
}
