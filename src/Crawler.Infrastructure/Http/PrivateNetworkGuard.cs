using System.Net;
using System.Net.Sockets;

namespace Crawler.Infrastructure.Http;

/// <summary>A fetch refused because the host resolves only to private / internal addresses.</summary>
public sealed class BlockedDestinationException(string message) : Exception(message);

/// <summary>
/// SSRF protection: the crawler fetches URLs that anyone can submit, so it must never reach internal
/// addresses — on AWS especially the instance metadata service (169.254.169.254), which can hand out
/// credentials. Used as the HTTP handler's ConnectCallback: the host is resolved here and the socket
/// connects to an allowed address directly, so a redirect or DNS rebinding can't sneak past the check.
/// </summary>
public static class PrivateNetworkGuard
{
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        await ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken);

    public static async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        var allowed = addresses.Where(address => !IsBlocked(address)).ToArray();
        if (allowed.Length == 0)
            throw new BlockedDestinationException($"{host} resolves to a private or internal network address; not crawled.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Loopback, private (RFC 1918 / ULA), link-local (incl. cloud metadata), CGNAT, multicast and reserved ranges.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,                       // "this network", private, loopback
                100 => b[1] >= 64 && b[1] <= 127,             // 100.64.0.0/10 carrier-grade NAT
                169 => b[1] == 254,                           // 169.254.0.0/16 link-local, cloud metadata
                172 => b[1] >= 16 && b[1] <= 31,              // 172.16.0.0/12 private
                192 => (b[1] == 168)                          // 192.168.0.0/16 private
                       || (b[1] == 0 && b[2] == 0),           // 192.0.0.0/24 IETF protocol assignments
                198 => b[1] == 18 || b[1] == 19,              // 198.18.0.0/15 benchmarking
                >= 224 => true,                               // multicast and reserved
                _ => false,
            };
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || (address.GetAddressBytes()[0] & 0xFE) == 0xFC; // fc00::/7 unique local
    }
}
