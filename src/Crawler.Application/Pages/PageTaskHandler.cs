using Crawler.Application.Abstractions;
using Crawler.Contracts;
using Crawler.Domain.Content;
using Crawler.Domain.Jobs;
using Crawler.Domain.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crawler.Application.Pages;

public enum PageTaskResult
{
    Processed,
    DuplicateIgnored,
    JobNotActive,
}

/// <summary>
/// Processes one <see cref="CrawlPageTask"/>: lease the page, fetch it, extract links,
/// compute the Domain Link Ratio and persist the outcome atomically.
/// Safe under at-least-once delivery — duplicates are detected by the lease and by the store.
/// </summary>
public sealed class PageTaskHandler(
    ICrawlStore store,
    IPageFetcher fetcher,
    IOptions<CrawlerOptions> options,
    ILogger<PageTaskHandler> logger)
{
    public async Task<PageTaskResult> HandleAsync(CrawlPageTask task, CancellationToken cancellationToken)
    {
        if (task.SchemaVersion != CrawlPageTask.CurrentSchemaVersion)
            throw new PoisonMessageException($"Unsupported schema version {task.SchemaVersion}.");

        var lease = await store.TryLeasePageAsync(task.PageId, options.Value.PageLease, cancellationToken);

        switch (lease.Outcome)
        {
            case LeaseOutcome.NotFound:
                throw new PoisonMessageException($"Page {task.PageId} does not exist.");
            case LeaseOutcome.AlreadyFinished:
                logger.LogInformation("Page {PageId} already processed; ignoring duplicate delivery", task.PageId);
                return PageTaskResult.DuplicateIgnored;
            case LeaseOutcome.JobNotActive:
                logger.LogInformation("Job {JobId} is no longer active; dropping page {PageId}", task.JobId, task.PageId);
                return PageTaskResult.JobNotActive;
            case LeaseOutcome.LeasedElsewhere:
                throw new TransientCrawlException($"Page {task.PageId} is being processed by another consumer.");
        }

        var page = lease.Page!;
        PageOutcome outcome;

        try
        {
            var fetch = await fetcher.FetchAsync(new Uri(page.Url), cancellationToken);
            outcome = BuildOutcome(page, fetch);
        }
        catch
        {
            await ReleaseQuietlyAsync(page.PageId);
            throw;
        }

        switch (await store.CompletePageAsync(outcome, cancellationToken))
        {
            case PageCompletion.AlreadyFinished:
                logger.LogWarning("Page {PageId} was finished by another consumer while we held it", page.PageId);
                return PageTaskResult.DuplicateIgnored;
            case PageCompletion.RecordedAsDuplicate:
                logger.LogInformation("Page {PageId} has the same content as an earlier page: {Url} recorded as duplicate",
                    page.PageId, page.Url);
                return PageTaskResult.Processed;
        }

        logger.LogInformation(
            "Page {PageId} {Status}: {Url} ({LinkCount} links, ratio {Ratio}, {ChildCount} children queued)",
            page.PageId, outcome.Status, page.Url, outcome.Links.Count, outcome.DomainLinkRatio, outcome.ChildUrls.Count);

        return PageTaskResult.Processed;
    }

    internal static PageOutcome BuildOutcome(LeasedPage page, FetchResult fetch)
    {
        if (fetch.Kind == FetchKind.HttpError)
            return PageOutcome.Failed(page.PageId, $"HTTP {fetch.StatusCode}", fetch.StatusCode);

        // The start page decides where the site lives: if it redirects to another host
        // (google.com → www.google.com), that host becomes the job's starting domain.
        // Any other page that redirects off the starting domain is outside the crawl.
        var rootHost = page.RootHost;
        string? newRootHost = null;
        if (!DomainLinkRatio.IsInternal(fetch.FinalUri.AbsoluteUri, rootHost))
        {
            if (page.Depth > 0)
            {
                return new PageOutcome(page.PageId, PageStatus.Skipped, fetch.StatusCode, fetch.ContentType,
                    Error: $"Redirected off-domain to {fetch.FinalUri.Host}");
            }

            rootHost = newRootHost = fetch.FinalUri.Host;
        }

        if (fetch.Kind == FetchKind.NotHtml)
        {
            return new PageOutcome(page.PageId, PageStatus.Skipped, fetch.StatusCode, fetch.ContentType,
                Error: $"Not HTML ({fetch.ContentType ?? "no content type"})", NewRootHost: newRootHost);
        }

        var links = LinkExtractor.Extract(fetch.Html!, fetch.FinalUri);
        var discovered = links
            .Select(url => new DiscoveredLink(url, DomainLinkRatio.IsInternal(url, rootHost)))
            .ToList();

        var children = page.Depth < page.MaxDepth
            ? discovered.Where(l => l.IsInternal).Select(l => l.Url).ToList()
            : [];

        return new PageOutcome(
            page.PageId,
            PageStatus.Completed,
            fetch.StatusCode,
            fetch.ContentType,
            DomainLinkRatio: DomainLinkRatio.Calculate(links, rootHost),
            Links: discovered,
            ChildUrls: children,
            ContentHash: ContentFingerprint.Compute(fetch.Html!),
            NewRootHost: newRootHost);
    }

    private async Task ReleaseQuietlyAsync(Guid pageId)
    {
        try
        {
            await store.ReleasePageAsync(pageId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The lease expires on its own; a retried delivery picks the page up after that.
            logger.LogWarning(ex, "Could not release lease on page {PageId}", pageId);
        }
    }
}
