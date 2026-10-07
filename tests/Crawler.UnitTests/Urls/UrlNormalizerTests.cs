using Crawler.Domain.Urls;

namespace Crawler.UnitTests.Urls;

public class UrlNormalizerTests
{
    private static readonly Uri Page = new("https://example.com/docs/guide/intro.html");

    [Theory]
    // relative paths
    [InlineData("setup.html", "https://example.com/docs/guide/setup.html")]
    [InlineData("./setup.html", "https://example.com/docs/guide/setup.html")]
    [InlineData("../api/", "https://example.com/docs/api/")]
    [InlineData("../../../../too-far", "https://example.com/too-far")]
    [InlineData("/about", "https://example.com/about")]
    [InlineData("//cdn.example.org/lib.js", "https://cdn.example.org/lib.js")]
    [InlineData("?page=2", "https://example.com/docs/guide/intro.html?page=2")]
    // already absolute
    [InlineData("https://other.com/x", "https://other.com/x")]
    [InlineData("http://example.com/plain", "http://example.com/plain")]
    public void Resolves_relative_and_absolute_links(string href, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(href, Page));

    [Theory]
    [InlineData("#section", "https://example.com/docs/guide/intro.html")]
    [InlineData("#", "https://example.com/docs/guide/intro.html")]
    [InlineData("/about#team", "https://example.com/about")]
    [InlineData("setup.html?x=1#top", "https://example.com/docs/guide/setup.html?x=1")]
    public void Drops_fragments(string href, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(href, Page));

    [Fact]
    public void Empty_href_refers_to_the_page_itself() =>
        Assert.Equal("https://example.com/docs/guide/intro.html", UrlNormalizer.Normalize("", Page));

    [Theory]
    [InlineData("HTTPS://EXAMPLE.COM/About", "https://example.com/About")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    [InlineData("http://example.com:8080/a", "http://example.com:8080/a")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/a/", "https://example.com/a/")]
    [InlineData("https://user:pass@example.com/a", "https://example.com/a")]
    [InlineData("https://example.com/a/./b/../c", "https://example.com/a/c")]
    [InlineData("  https://example.com/padded  ", "https://example.com/padded")]
    [InlineData("https://example.com/search?b=2&a=1", "https://example.com/search?b=2&a=1")]
    public void Canonicalizes_case_ports_and_paths(string href, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(href, Page));

    [Theory]
    [InlineData("mailto:someone@example.com")]
    [InlineData("tel:+15551234567")]
    [InlineData("javascript:void(0)")]
    [InlineData("data:text/html,hello")]
    [InlineData("ftp://example.com/file")]
    [InlineData(null)]
    public void Ignores_non_http_links(string? href) =>
        Assert.Null(UrlNormalizer.Normalize(href, Page));

    [Fact]
    public void Rejects_urls_longer_than_the_limit()
    {
        var longPath = "/" + new string('a', UrlNormalizer.MaxUrlLength);
        Assert.Null(UrlNormalizer.Normalize(longPath, Page));
    }

    [Theory]
    [InlineData("https://Example.com/Path#frag", "https://example.com/Path")]
    [InlineData("not a url", null)]
    [InlineData("/relative-only", null)]
    [InlineData("mailto:a@b.com", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalizes_absolute_job_urls(string? url, string? expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(url));

    [Theory]
    // bare hosts get https:// (like a browser address bar)
    [InlineData("google.com", "https://google.com/")]
    [InlineData("  Example.COM/Docs?x=1#top ", "https://example.com/Docs?x=1")]
    [InlineData("www.example.com", "https://www.example.com/")]
    [InlineData("example.com:8443/admin", "https://example.com:8443/admin")]
    [InlineData("localhost:3000", "https://localhost:3000/")]
    // an explicit scheme is kept
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("HTTPS://example.com/a", "https://example.com/a")]
    // still rejected: other schemes, relative paths, junk
    [InlineData("mailto:someone@example.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("ftp://example.com/file", null)]
    [InlineData("/relative/path", null)]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalizes_what_people_type_as_a_start_url(string? input, string? expected) =>
        Assert.Equal(expected, UrlNormalizer.NormalizeUserInput(input));

    [Fact]
    public void Equivalent_spellings_normalize_to_the_same_key()
    {
        string?[] variants =
        [
            UrlNormalizer.Normalize("https://example.com/about", Page),
            UrlNormalizer.Normalize("/about#team", Page),
            UrlNormalizer.Normalize("HTTPS://EXAMPLE.COM:443/about", Page),
            UrlNormalizer.Normalize("../../about", Page),
        ];

        Assert.All(variants, v => Assert.Equal("https://example.com/about", v));
    }
}
