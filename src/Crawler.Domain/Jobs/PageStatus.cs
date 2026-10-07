namespace Crawler.Domain.Jobs;

public enum PageStatus
{
    /// <summary>Discovered and queued, not yet picked up.</summary>
    Pending,

    /// <summary>Leased by a worker; the lease expires if the worker dies.</summary>
    Processing,

    /// <summary>Fetched and parsed as HTML.</summary>
    Completed,

    /// <summary>Fetched but not crawlable (non-HTML content, redirected off-domain).</summary>
    Skipped,

    /// <summary>Permanent failure (4xx, retries exhausted, dead-lettered).</summary>
    Failed,
}

public static class PageStatusExtensions
{
    public static bool IsTerminal(this PageStatus status) =>
        status is PageStatus.Completed or PageStatus.Skipped or PageStatus.Failed;
}
