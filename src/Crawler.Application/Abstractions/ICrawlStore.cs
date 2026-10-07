using Crawler.Domain.Jobs;

namespace Crawler.Application.Abstractions;

/// <summary>
/// Write side of crawl persistence. Each method is one atomic, idempotent unit of work,
/// so a message delivered more than once can never create duplicate rows.
/// </summary>
public interface ICrawlStore
{
    /// <summary>Creates the job, its root page and the root task (outbox) in one transaction.</summary>
    Task<CreatedJob> CreateJobAsync(NewJob job, CancellationToken cancellationToken);

    /// <summary>Takes ownership of a page for processing, if it is still pending and its job is active.</summary>
    Task<LeaseResult> TryLeasePageAsync(Guid pageId, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>Gives a leased page back (status Pending) so a retried delivery can take it immediately.</summary>
    Task ReleasePageAsync(Guid pageId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a page result in one transaction: page row, edges, newly claimed child pages
    /// (respecting the job's page cap), their outbox tasks, job counters and job completion.
    /// A Completed page whose <see cref="PageOutcome.ContentHash"/> matches a page already crawled in
    /// the job is stored as <see cref="PageStatus.Duplicate"/> instead, and not expanded.
    /// </summary>
    Task<PageCompletion> CompletePageAsync(PageOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Cancels a Pending or Running job. Workers drop its remaining tasks.</summary>
    Task<CancelOutcome> CancelJobAsync(Guid jobId, CancellationToken cancellationToken);
}

public enum PageCompletion
{
    /// <summary>Stored as given.</summary>
    Recorded,

    /// <summary>Same content as an earlier page of the job: stored as Duplicate, links not followed.</summary>
    RecordedAsDuplicate,

    /// <summary>The page was already in a terminal state (duplicate message delivery); nothing written.</summary>
    AlreadyFinished,
}

public enum CancelOutcome
{
    Canceled,
    NotFound,
    NotActive,
}

public sealed record NewJob(string Url, string RootHost, int MaxDepth, int MaxPages);

public sealed record CreatedJob(Guid JobId, Guid RootPageId);

public enum LeaseOutcome
{
    Leased,
    AlreadyFinished,
    LeasedElsewhere,
    JobNotActive,
    NotFound,
}

public sealed record LeasedPage(Guid PageId, Guid JobId, string Url, int Depth, int MaxDepth, string RootHost);

public sealed record LeaseResult(LeaseOutcome Outcome, LeasedPage? Page = null);

public sealed record DiscoveredLink(string Url, bool IsInternal);

public sealed record PageOutcome(
    Guid PageId,
    PageStatus Status,
    int? HttpStatus = null,
    string? ContentType = null,
    string? Error = null,
    double? DomainLinkRatio = null,
    IReadOnlyList<DiscoveredLink>? Links = null,
    IReadOnlyList<string>? ChildUrls = null,
    string? ContentHash = null)
{
    public IReadOnlyList<DiscoveredLink> Links { get; init; } = Links ?? [];

    public IReadOnlyList<string> ChildUrls { get; init; } = ChildUrls ?? [];

    public static PageOutcome Failed(Guid pageId, string error, int? httpStatus = null) =>
        new(pageId, PageStatus.Failed, httpStatus, Error: error);
}
