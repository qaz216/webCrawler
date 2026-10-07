using Crawler.Domain.Jobs;

namespace Crawler.Application.Abstractions;

/// <summary>Read side for the API: job status, page tree data and history.</summary>
public interface ICrawlQueries
{
    Task<JobDetails?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>All pages of a job in one query; the tree is assembled in memory.</summary>
    Task<IReadOnlyList<PageRecord>> GetPagesAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>Most recent first.</summary>
    Task<PagedResult<JobListItem>> ListJobsAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page with its outgoing links, split into in-domain and outbound; null if not in the job.</summary>
    Task<PageLinks?> GetPageLinksAsync(Guid jobId, Guid pageId, CancellationToken cancellationToken);
}

public sealed record PageLinks(
    Guid JobId,
    Guid PageId,
    string Url,
    PageStatus Status,
    int? HttpStatus,
    int Depth,
    double? DomainLinkRatio,
    string? Error,
    string? ParentUrl,
    string StartingDomain,
    IReadOnlyList<InternalLink> InternalLinks,
    IReadOnlyList<ExternalLink> ExternalLinks);

/// <param name="PageId">The target's page in this job, if it was discovered (null: beyond the depth or page limit).</param>
public sealed record InternalLink(string Url, Guid? PageId, PageStatus? Status, double? DomainLinkRatio, int? OutgoingLinkCount);

public sealed record ExternalLink(string Url, string Host);

/// <param name="StartingDomain">
/// Root domain whose links count as internal (subdomains included): the job URL's, or — if the start
/// page redirected to a different site — that site's.
/// </param>
/// <param name="RedirectedFrom">The host that was entered, when the start page redirected to a different site; otherwise null.</param>
public sealed record JobDetails(
    Guid JobId,
    string Url,
    string StartingDomain,
    string? RedirectedFrom,
    JobStatus Status,
    int MaxDepth,
    int MaxPages,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureReason,
    JobProgress Progress);

/// <param name="Completed">Crawled successfully, including Skipped (non-HTML, off-domain redirect).</param>
/// <param name="Duplicates">Fetched, but identical in content to a page already crawled; not expanded.</param>
public sealed record JobProgress(int Discovered, int Completed, int Failed, int Duplicates)
{
    public int Pending => Math.Max(0, Discovered - Completed - Failed - Duplicates);

    /// <summary>Share of discovered pages that are finished. The total grows while crawling, so this is a lower bound.</summary>
    public double Percent => Discovered == 0 ? 0 : Math.Round(100.0 * (Discovered - Pending) / Discovered, 1);
}

/// <param name="AverageDomainLinkRatio">Mean Domain Link Ratio of the job's crawled pages; null until one has been crawled.</param>
public sealed record JobListItem(
    Guid JobId,
    string Url,
    JobStatus Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int PagesDiscovered,
    double? AverageDomainLinkRatio);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record PageRecord(
    Guid PageId,
    Guid? ParentPageId,
    string Url,
    int Depth,
    PageStatus Status,
    int? HttpStatus,
    string? Error,
    double? DomainLinkRatio,
    int? OutgoingLinkCount,
    Guid? DuplicateOfPageId = null,
    string? DuplicateOfUrl = null,
    DateTime? FinishedAt = null);
