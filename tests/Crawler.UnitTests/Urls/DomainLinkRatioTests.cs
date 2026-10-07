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
    [InlineData("https://www.example.com/a", false)]
    [InlineData("https://blog.example.com/a", false)]
    [InlineData("https://example.com.evil.net/a", false)]
    [InlineData("https://EXAMPLE.com/a", true)]
    [InlineData("http://example.com:8080/a", true)]
    public void Only_the_exact_starting_host_is_internal(string link, bool expected) =>
        Assert.Equal(expected, DomainLinkRatio.IsInternal(link, Host));

    [Fact]
    public void Starting_host_comparison_is_case_insensitive() =>
        Assert.Equal(1.0, DomainLinkRatio.Calculate(["https://example.com/a"], "Example.COM"));

    [Fact]
    public void Blank_starting_host_is_rejected() =>
        Assert.Throws<ArgumentException>(() => DomainLinkRatio.Calculate(["https://example.com/"], " "));
}
