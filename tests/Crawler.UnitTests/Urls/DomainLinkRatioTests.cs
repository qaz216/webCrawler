using Crawler.Domain.Urls;

namespace Crawler.UnitTests.Urls;

public class DomainLinkRatioTests
{
    private const string Host = "example.com";

    [Fact]
    public void All_internal_links_give_one() =>
        Assert.Equal(1.0, DomainLinkRatio.Calculate(
            ["https://example.com/a", "https://example.com/b"], Host));

    [Fact]
    public void All_external_links_give_zero() =>
        Assert.Equal(0.0, DomainLinkRatio.Calculate(
            ["https://other.com/a", "https://another.org/b"], Host));

    [Fact]
    public void Mixed_links_give_the_internal_fraction() =>
        Assert.Equal(0.75, DomainLinkRatio.Calculate(
            ["https://example.com/a", "https://example.com/b", "http://example.com/c", "https://other.com/"], Host));

    [Fact]
    public void No_links_give_zero() =>
        Assert.Equal(0.0, DomainLinkRatio.Calculate([], Host));

    [Fact]
    public void Duplicate_links_count_once()
    {
        // 2 distinct internal + 1 distinct external = 2/3, not 3/4.
        var ratio = DomainLinkRatio.Calculate(
            ["https://example.com/a", "https://example.com/a", "https://example.com/b", "https://other.com/"], Host);

        Assert.Equal(2.0 / 3.0, ratio, precision: 10);
    }

    [Theory]
    [InlineData("https://www.example.com/a", true)]       // subdomains of the same root domain
    [InlineData("https://blog.example.com/a", true)]
    [InlineData("https://EXAMPLE.com/a", true)]
    [InlineData("http://example.com:8080/a", true)]
    [InlineData("https://example.com.evil.net/a", false)] // other sites
    [InlineData("https://example.org/a", false)]
    [InlineData("https://www.bbc.co.uk/a", false)]
    [InlineData("https://example.co.uk/a", false)]
    public void Links_within_the_starting_root_domain_are_internal(string link, bool expected) =>
        Assert.Equal(expected, DomainLinkRatio.IsInternal(link, Host));

    [Fact]
    public void Subdomain_links_count_as_internal_in_the_ratio()
    {
        // From www.newsmax.com: www., w3., ir. and the bare domain are inside; .uk and other sites outside.
        var ratio = DomainLinkRatio.Calculate(
        [
            "https://www.newsmax.com/a", "https://w3.newsmax.com/b", "https://ir.newsmax.com/",
            "https://newsmax.com/c", "https://www.bbc.co.uk/", "https://facebook.com/Newsmax",
        ], "www.newsmax.com");

        Assert.Equal(4.0 / 6.0, ratio, precision: 10);
    }

    [Fact]
    public void Starting_host_comparison_is_case_insensitive() =>
        Assert.Equal(1.0, DomainLinkRatio.Calculate(["https://example.com/a"], "Example.COM"));

    [Fact]
    public void Blank_starting_host_is_rejected() =>
        Assert.Throws<ArgumentException>(() => DomainLinkRatio.Calculate(["https://example.com/"], " "));
}
