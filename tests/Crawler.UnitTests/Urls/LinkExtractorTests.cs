using Crawler.Domain.Urls;

namespace Crawler.UnitTests.Urls;

public class LinkExtractorTests
{
    private static readonly Uri Page = new("https://example.com/blog/post.html");

    [Fact]
    public void Extracts_distinct_normalized_links_in_document_order()
    {
        const string html = """
            <html><body>
              <nav><a href="/">Home</a> <a href="/about">About</a></nav>
              <a href="next.html">Next</a>
              <a href="https://other.com/x">External</a>
              <footer><a href="/about#team">About again</a> <a href="/">Home again</a></footer>
            </body></html>
            """;

        var links = LinkExtractor.Extract(html, Page);

        Assert.Equal(
            ["https://example.com/", "https://example.com/about", "https://example.com/blog/next.html", "https://other.com/x"],
            links);
    }

    [Fact]
    public void Skips_non_http_and_missing_hrefs()
    {
        const string html = """
            <a href="mailto:me@example.com">Mail</a>
            <a href="tel:123">Call</a>
            <a href="javascript:void(0)">JS</a>
            <a>No href</a>
            <a name="anchor-target">Named anchor</a>
            <a href="/ok">Ok</a>
            """;

        Assert.Equal(["https://example.com/ok"], LinkExtractor.Extract(html, Page));
    }

    [Fact]
    public void Honours_base_href()
    {
        const string html = """
            <html><head><base href="https://example.com/docs/"></head>
            <body><a href="page.html">Page</a></body></html>
            """;

        Assert.Equal(["https://example.com/docs/page.html"], LinkExtractor.Extract(html, Page));
    }

    [Fact]
    public void Relative_base_href_is_resolved_against_the_page()
    {
        const string html = """<base href="/v2/"><a href="start">Start</a>""";

        Assert.Equal(["https://example.com/v2/start"], LinkExtractor.Extract(html, Page));
    }

    [Fact]
    public void Includes_image_map_areas()
    {
        const string html = """<map><area href="/region" alt="r"></map>""";

        Assert.Equal(["https://example.com/region"], LinkExtractor.Extract(html, Page));
    }

    [Fact]
    public void Tolerates_malformed_html()
    {
        const string html = """<div><a href="/one">One<p><a href=/two>Two</div></span>""";

        Assert.Equal(["https://example.com/one", "https://example.com/two"], LinkExtractor.Extract(html, Page));
    }

    [Fact]
    public void Page_without_links_returns_empty() =>
        Assert.Empty(LinkExtractor.Extract("<html><body><p>Nothing here</p></body></html>", Page));
}
