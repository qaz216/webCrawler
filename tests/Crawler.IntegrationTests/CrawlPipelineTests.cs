using Crawler.Application;
using Crawler.Application.Pages;
using Crawler.IntegrationTests.Infrastructure;

namespace Crawler.IntegrationTests;

/// <summary>
/// End-to-end crawl of the local fixture site through the real handler, store, outbox and
/// fetcher against PostgreSQL. Expected values are derived from Fixtures/site by hand.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CrawlPipelineTests(PostgresFixture db)
{
    [Fact]
    public async Task Crawls_fixture_site_to_completion()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync();

        await harness.RunToCompletionAsync(job.JobId);

        var summary = await harness.GetJobAsync(job.JobId);
        Assert.Equal("Completed", summary.Status);
        Assert.NotNull(summary.StartedAt);
        Assert.NotNull(summary.CompletedAt);
        Assert.Equal(11, summary.PagesDiscovered);
        Assert.Equal(9, summary.PagesCompleted); // includes the skipped PDF
        Assert.Equal(1, summary.PagesFailed);    // the 404
        Assert.Equal(1, summary.PagesDuplicate); // /index.html = same content as /

        var pages = await harness.GetPagesAsync(job.JobId);
        Assert.Equal(
            ["/", "/about.html", "/blog/post.html", "/files/manual.pdf", "/flaky.html", "/index.html", "/missing.html",
             "/products/", "/products/a.html", "/products/b.html", "/team.html"],
            pages.Keys.Order(StringComparer.Ordinal));

        // Depth limit: c.html is linked from a depth-2 page, so it is never discovered or fetched.
        Assert.Equal(0, harness.Site.RequestCount("/products/c.html"));

        // Home: 8 distinct links (dup About, mailto and fragment collapse), 7 internal.
        AssertPage(pages["/"], depth: 0, "Completed", ratio: 0.875m, links: 8, parent: null);
        AssertPage(pages["/about.html"], depth: 1, "Completed", ratio: 0.75m, links: 4, parent: "/");
        AssertPage(pages["/products/"], depth: 1, "Completed", ratio: 1m, links: 4, parent: "/");
        AssertPage(pages["/blog/post.html"], depth: 1, "Completed", ratio: 0.3333m, links: 3, parent: "/"); // www./blog. are external
        AssertPage(pages["/flaky.html"], depth: 1, "Completed", ratio: 1m, links: 1, parent: "/");
        AssertPage(pages["/team.html"], depth: 2, "Completed", ratio: 0m, links: 0, parent: "/about.html");
        AssertPage(pages["/products/a.html"], depth: 2, "Completed", ratio: 1m, links: 1, parent: "/products/");
        AssertPage(pages["/products/b.html"], depth: 2, "Completed", ratio: 0m, links: 1, parent: "/products/");

        Assert.Equal("Failed", pages["/missing.html"].Status);
        Assert.Equal(404, pages["/missing.html"].HttpStatus);
        Assert.Equal("Skipped", pages["/files/manual.pdf"].Status);

        // Same HTML as the home page under another URL: recorded once, the alias points at it.
        var alias = pages["/index.html"];
        Assert.Equal("Duplicate", alias.Status);
        Assert.Equal("https://site.test/", alias.DuplicateOfUrl);
        Assert.Equal("/about.html", new Uri(alias.ParentUrl!).PathAndQuery);
        Assert.Null(alias.DomainLinkRatio);

        // Transient 503 was retried and succeeded on the second attempt.
        Assert.Equal(2, pages["/flaky.html"].Attempts);

        // No URL fetched twice, even though several are linked from multiple pages.
        Assert.All(harness.Site.Requests.Where(r => r.Key != "/flaky.html"), r => Assert.Equal(1, r.Value));
    }

    [Fact]
    public async Task Pages_with_identical_content_are_crawled_once_even_when_processed_concurrently()
    {
        // Start at /about.html: it links to both "/" and "/index.html", which serve identical HTML.
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync("/about.html", maxDepth: 2);
        var root = Assert.Single(await harness.DequeueTasksAsync(job.JobId));
        await harness.Handler.HandleAsync(root, CancellationToken.None);

        // Deliver all children at once, so the two aliases race each other.
        var children = await harness.DequeueTasksAsync(job.JobId);
        await Task.WhenAll(children.Select(task => harness.Handler.HandleAsync(task, CancellationToken.None)));

        var pages = await harness.GetPagesAsync(job.JobId);
        var aliases = new[] { pages["/"], pages["/index.html"] };
        var original = Assert.Single(aliases, p => p.Status == "Completed");
        var duplicate = Assert.Single(aliases, p => p.Status == "Duplicate");
        Assert.Equal(original.Url, duplicate.DuplicateOfUrl);

        // Only the original is expanded: its links became pages, the duplicate's did not.
        Assert.True(original.ChildCount > 0);
        Assert.Equal(0, duplicate.ChildCount);

        await harness.RunToCompletionAsync(job.JobId);
        var summary = await harness.GetJobAsync(job.JobId);
        Assert.Equal("Completed", summary.Status);
        Assert.Equal(1, summary.PagesDuplicate);
        Assert.Equal(summary.PagesDiscovered, summary.PagesCompleted + summary.PagesFailed + summary.PagesDuplicate);
    }

    [Fact]
    public async Task Start_page_redirect_to_another_host_moves_the_starting_domain()
    {
        // Like google.com → www.google.com: alias.test redirects to site.test.
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAtAsync(new Uri($"https://{FixtureSiteHandler.RedirectingHost}/"));

        await harness.RunToCompletionAsync(job.JobId);

        var summary = await harness.GetJobAsync(job.JobId);
        Assert.Equal("Completed", summary.Status);
        Assert.Equal(FixtureSiteHandler.Host, summary.RootHost);

        // The whole site was crawled, not skipped as "off-domain".
        var pages = await harness.GetPagesAsync(job.JobId);
        var root = pages["https://alias.test/"];
        Assert.Equal("Completed", root.Status);
        Assert.Equal(0.875m, root.DomainLinkRatio); // judged against site.test
        Assert.Equal("Completed", pages["/team.html"].Status);
        Assert.Equal(2, pages["/team.html"].Depth);

        // site.test/ (the redirect target, linked as "#top") and /index.html have the root's content:
        // fetched once each, recorded as duplicates, not crawled again.
        Assert.Equal("Duplicate", pages["/"].Status);
        Assert.Equal("Duplicate", pages["/index.html"].Status);
        Assert.Equal(2, summary.PagesDuplicate);
        Assert.Equal(1, summary.PagesFailed);
        Assert.Equal(12, summary.PagesDiscovered);
    }

    [Fact]
    public async Task Duplicate_delivery_creates_no_duplicate_rows()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync(maxDepth: 1);
        var rootTask = Assert.Single(await harness.DequeueTasksAsync(job.JobId));

        Assert.Equal(PageTaskResult.Processed, await harness.Handler.HandleAsync(rootTask, CancellationToken.None));
        var afterFirst = await harness.CountRowsAsync(job.JobId);

        Assert.Equal(PageTaskResult.DuplicateIgnored, await harness.Handler.HandleAsync(rootTask, CancellationToken.None));
        var afterSecond = await harness.CountRowsAsync(job.JobId);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(1, harness.Site.RequestCount("/"));
        // root + 6 children: of the 7 internal links, "#top" points back at the root itself.
        Assert.Equal(7, (await harness.GetJobAsync(job.JobId)).PagesDiscovered);
    }

    [Fact]
    public async Task Concurrent_duplicate_deliveries_process_the_page_once()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync(maxDepth: 1);
        var rootTask = Assert.Single(await harness.DequeueTasksAsync(job.JobId));

        var deliveries = Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                return (object)await harness.Handler.HandleAsync(rootTask, CancellationToken.None);
            }
            catch (TransientCrawlException ex)
            {
                return ex; // leased by a concurrent delivery: would be retried by the broker
            }
        });
        var results = await Task.WhenAll(deliveries);

        Assert.Single(results, r => r is PageTaskResult.Processed);
        Assert.DoesNotContain(results, r => r is PageTaskResult.JobNotActive);
        Assert.Equal(1, harness.Site.RequestCount("/"));

        var (pages, links, outbox) = await harness.CountRowsAsync(job.JobId);
        Assert.Equal(7, pages);   // root + 6 new children (the self-link is not a new page)
        Assert.Equal(8, links);   // every distinct outgoing link, internal and external
        Assert.Equal(7, outbox);  // root task + one task per new child
    }

    [Fact]
    public async Task Page_cap_limits_discovered_pages()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync(maxPages: 4);

        await harness.RunToCompletionAsync(job.JobId);

        var summary = await harness.GetJobAsync(job.JobId);
        Assert.Equal("Completed", summary.Status);
        Assert.Equal(4, summary.PagesDiscovered);
        Assert.Equal(4, (await harness.CountRowsAsync(job.JobId)).Pages);
        Assert.Equal(4, harness.Site.Requests.Values.Sum());
    }

    [Fact]
    public async Task Start_page_failure_fails_the_job()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync("/missing.html");

        await harness.RunToCompletionAsync(job.JobId);

        var summary = await harness.GetJobAsync(job.JobId);
        Assert.Equal("Failed", summary.Status);
        Assert.Contains("HTTP 404", summary.FailureReason);
        Assert.NotNull(summary.CompletedAt);
    }

    [Fact]
    public async Task Tasks_for_a_canceled_job_are_dropped()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync();
        var rootTask = Assert.Single(await harness.DequeueTasksAsync(job.JobId));

        await harness.SetJobStatusAsync(job.JobId, "Canceled");

        Assert.Equal(PageTaskResult.JobNotActive, await harness.Handler.HandleAsync(rootTask, CancellationToken.None));
        Assert.Equal(0, harness.Site.RequestCount("/"));
    }

    [Fact]
    public async Task Unknown_page_is_a_poison_message()
    {
        var harness = new CrawlHarness(db.DataSource);
        var job = await harness.StartJobAsync();
        var rootTask = Assert.Single(await harness.DequeueTasksAsync(job.JobId));

        await Assert.ThrowsAsync<PoisonMessageException>(() =>
            harness.Handler.HandleAsync(rootTask with { PageId = Guid.NewGuid() }, CancellationToken.None));
        await Assert.ThrowsAsync<PoisonMessageException>(() =>
            harness.Handler.HandleAsync(rootTask with { SchemaVersion = 99 }, CancellationToken.None));
    }

    private static void AssertPage(
        CrawlHarness.PageRow page, int depth, string status, decimal ratio, int links, string? parent)
    {
        Assert.Equal(depth, page.Depth);
        Assert.Equal(status, page.Status);
        Assert.Equal(ratio, page.DomainLinkRatio);
        Assert.Equal(links, page.OutgoingLinkCount);
        Assert.Equal(parent, page.ParentUrl is null ? null : new Uri(page.ParentUrl).PathAndQuery);
    }
}
