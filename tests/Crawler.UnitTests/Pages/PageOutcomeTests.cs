using Crawler.Application.Abstractions;
using Crawler.Application.Pages;
using Crawler.Domain.Jobs;

namespace Crawler.UnitTests.Pages;

/// <summary>How a fetched page becomes an outcome — in particular the redirect rules.</summary>
public class PageOutcomeTests
{
    private const string Html = """
        <a href="/about">About</a>
        <a href="https://shop.example.com/">Shop (subdomain)</a>
        <a href="https://other.test/">Elsewhere</a>
        """;

    private static LeasedPage Page(int depth) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "https://example.com/", depth, MaxDepth: 2, RootHost: "example.com");

    private static FetchResult HtmlAt(string finalUrl) =>
        FetchResult.FromHtml(new Uri(finalUrl), 200, "text/html", Html);

    [Fact]
    public void Redirect_to_a_subdomain_stays_on_the_site()
    {
        // example.com → www.example.com: same root domain, so nothing changes.
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 0), HtmlAt("https://www.example.com/"));

        Assert.Equal(PageStatus.Completed, outcome.Status);
        Assert.Null(outcome.NewRootHost);
        Assert.Equal(2.0 / 3.0, outcome.DomainLinkRatio!.Value, precision: 10); // /about and shop. are internal
        Assert.Equal(["https://www.example.com/about", "https://shop.example.com/"], outcome.ChildUrls);
    }

    [Fact]
    public void Start_page_redirected_to_a_different_site_moves_the_starting_domain()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 0), HtmlAt("https://www.example.net/"));

        Assert.Equal(PageStatus.Completed, outcome.Status);
        Assert.Equal("www.example.net", outcome.NewRootHost);
        // Judged against example.net now: only the relative /about link is internal.
        Assert.Equal(1.0 / 3.0, outcome.DomainLinkRatio!.Value, precision: 10);
        Assert.Equal(["https://www.example.net/about"], outcome.ChildUrls);
    }

    [Fact]
    public void Other_pages_redirected_to_a_different_site_are_skipped()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 1), HtmlAt("https://www.example.net/"));

        Assert.Equal(PageStatus.Skipped, outcome.Status);
        Assert.Equal("Redirected off-domain to www.example.net", outcome.Error);
        Assert.Null(outcome.NewRootHost);
        Assert.Empty(outcome.ChildUrls);
    }

    [Fact]
    public void Other_pages_redirected_to_a_subdomain_are_crawled()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 1), HtmlAt("https://blog.example.com/post"));

        Assert.Equal(PageStatus.Completed, outcome.Status);
    }

    [Fact]
    public void Non_html_start_page_still_records_its_redirect_site()
    {
        var fetch = FetchResult.NotHtml(new Uri("https://www.example.net/file.pdf"), 200, "application/pdf");

        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 0), fetch);

        Assert.Equal(PageStatus.Skipped, outcome.Status);
        Assert.Equal("www.example.net", outcome.NewRootHost);
    }

    [Fact]
    public void Http_errors_fail_the_page()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 1), FetchResult.HttpError(new Uri("https://example.com/x"), 404));

        Assert.Equal(PageStatus.Failed, outcome.Status);
        Assert.Equal(404, outcome.HttpStatus);
    }
}
