using Crawler.Domain.Urls;

namespace Crawler.UnitTests.Urls;

public class RootDomainTests
{
    [Theory]
    [InlineData("newsmax.com", "newsmax.com")]
    [InlineData("www.newsmax.com", "newsmax.com")]
    [InlineData("w3.newsmax.com", "newsmax.com")]
    [InlineData("ir.newsmax.com", "newsmax.com")]
    [InlineData("a.b.c.newsmax.com", "newsmax.com")]
    [InlineData("WWW.NewsMax.COM.", "newsmax.com")]
    // Two-part country suffixes: the root is one label more, never the bare suffix.
    [InlineData("www.bbc.co.uk", "bbc.co.uk")]
    [InlineData("news.bbc.co.uk", "bbc.co.uk")]
    [InlineData("shop.example.com.au", "example.com.au")]
    [InlineData("www.ynet.co.il", "ynet.co.il")]
    // Plain country domains.
    [InlineData("www.example.uk", "example.uk")]
    [InlineData("www.spiegel.de", "spiegel.de")]
    // Hosts that are their own root.
    [InlineData("localhost", "localhost")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("[::1]", "[::1]")]
    public void Finds_the_root_domain(string host, string expected) =>
        Assert.Equal(expected, RootDomain.Of(host));

    [Theory]
    // From newsmax.com, only newsmax.com and its subdomains are the same site.
    [InlineData("newsmax.com", "www.newsmax.com", true)]
    [InlineData("newsmax.com", "w3.newsmax.com", true)]
    [InlineData("www.newsmax.com", "ir.newsmax.com", true)]
    [InlineData("newsmax.com", "newsmaxtv.com", false)]
    [InlineData("newsmax.com", "newsmax.com.evil.net", false)]
    [InlineData("newsmax.com", "bbc.co.uk", false)]
    [InlineData("newsmax.com", "www.example.uk", false)]
    [InlineData("newsmax.com", "newsmax.co.uk", false)]
    // A .uk starting site still treats other .uk sites as outside.
    [InlineData("www.bbc.co.uk", "news.bbc.co.uk", true)]
    [InlineData("www.bbc.co.uk", "www.itv.co.uk", false)]
    [InlineData("www.bbc.co.uk", "co.uk", false)]
    public void Same_site_means_same_root_domain(string a, string b, bool expected) =>
        Assert.Equal(expected, RootDomain.SameSite(a, b));
}
