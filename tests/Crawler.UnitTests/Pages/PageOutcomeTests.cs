using Crawler.Application.Abstractions;
using Crawler.Application.Pages;
using Crawler.Domain.Jobs;

namespace Crawler.UnitTests.Pages;

/// <summary>How a fetched page becomes an outcome — in particular the redirect rules.</summary>
public class PageOutcomeTests
{
    private const string Html = """
        <a href="/about">About</a>
        <a href="https://www.example.com/contact">Contact</a>
        <a href="https://other.test/">Elsewhere</a>
        """;

    private static LeasedPage Page(int depth) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "https://example.com/", depth, MaxDepth: 2, RootHost: "example.com");

    private static FetchResult HtmlAt(string finalUrl) =>
        FetchResult.FromHtml(new Uri(finalUrl), 200, "text/html", Html);

    [Fact]
    public void Start_page_redirected_to_another_host_makes_that_host_the_starting_domain()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 0), HtmlAt("https://www.example.com/"));

        Assert.Equal(PageStatus.Completed, outcome.Status);
        Assert.Equal("www.example.com", outcome.NewRootHost);

        // Links are judged against the new host: /about and /contact are internal, other.test is not.
        Assert.Equal(2.0 / 3.0, outcome.DomainLinkRatio!.Value, precision: 10);
        Assert.Equal(["https://www.example.com/about", "https://www.example.com/contact"], outcome.ChildUrls);
    }

    [Fact]
    public void Start_page_on_the_same_host_keeps_the_starting_domain()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 0), HtmlAt("https://example.com/home"));

        Assert.Null(outcome.NewRootHost);
        Assert.Equal(["https://example.com/about"], outcome.ChildUrls);
    }

    [Fact]
    public void Other_pages_redirected_off_the_starting_domain_are_skipped()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 1), HtmlAt("https://www.example.com/"));

        Assert.Equal(PageStatus.Skipped, outcome.Status);
        Assert.Equal("Redirected off-domain to www.example.com", outcome.Error);
        Assert.Null(outcome.NewRootHost);
        Assert.Empty(outcome.ChildUrls);
    }

    [Fact]
    public void Non_html_start_page_still_records_its_redirect_host()
    {
        var fetch = FetchResult.NotHtml(new Uri("https://www.example.com/file.pdf"), 200, "application/pdf");

        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 0), fetch);

        Assert.Equal(PageStatus.Skipped, outcome.Status);
        Assert.Equal("www.example.com", outcome.NewRootHost);
    }

    [Fact]
    public void Http_errors_fail_the_page()
    {
        var outcome = PageTaskHandler.BuildOutcome(Page(depth: 1), FetchResult.HttpError(new Uri("https://example.com/x"), 404));

        Assert.Equal(PageStatus.Failed, outcome.Status);
        Assert.Equal(404, outcome.HttpStatus);
    }
}
