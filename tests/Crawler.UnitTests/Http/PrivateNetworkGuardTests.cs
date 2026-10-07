using System.Net;
using Crawler.Application.Abstractions;
using Crawler.Infrastructure.Http;

namespace Crawler.UnitTests.Http;

public class PrivateNetworkGuardTests
{
    [Theory]
    [InlineData("169.254.169.254")] // AWS / cloud instance metadata
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]      // carrier-grade NAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]       // multicast
    [InlineData("::1")]
    [InlineData("fe80::1")]         // link-local
    [InlineData("fd00:ec2::254")]   // AWS IPv6 metadata (unique local)
    [InlineData("::ffff:169.254.169.254")] // IPv4-mapped
    public void Blocks_internal_addresses(string address) =>
        Assert.True(PrivateNetworkGuard.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]      // just outside 172.16.0.0/12
    [InlineData("100.128.0.1")]     // just outside 100.64.0.0/10
    [InlineData("2606:4700:4700::1111")]
    public void Allows_public_addresses(string address) =>
        Assert.False(PrivateNetworkGuard.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("http://127.0.0.1:1/")]
    [InlineData("http://localhost:1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task Fetcher_refuses_internal_destinations_without_connecting(string url)
    {
        using var handler = new SocketsHttpHandler { ConnectCallback = PrivateNetworkGuard.ConnectAsync };
        var fetcher = new HttpPageFetcher(new HttpClient(handler));

        var result = await fetcher.FetchAsync(new Uri(url), CancellationToken.None);

        Assert.Equal(FetchKind.Refused, result.Kind);
        Assert.Contains("private or internal network address", result.Error);
    }
}
