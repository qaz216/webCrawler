using Crawler.Application.Abstractions;
using Crawler.Domain.Jobs;
using Dapper;
using Npgsql;

namespace Crawler.Infrastructure.Persistence;

/// <summary>
/// Read queries for the API. Status polling is a primary-key lookup (counters are denormalized),
/// the tree is one query on the (job_id, url) unique index, and history uses ix_crawl_jobs_created_at.
/// </summary>
public sealed class CrawlQueries(NpgsqlDataSource dataSource) : ICrawlQueries
{
    static CrawlQueries() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public async Task<JobDetails?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition("""
            SELECT id, url, status, max_depth, max_pages, created_at, started_at, completed_at, failure_reason,
                   pages_discovered, pages_completed, pages_failed, pages_duplicate
              FROM crawl_jobs
             WHERE id = @jobId
            """,
            new { jobId }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new JobDetails(row.Id, row.Url, Enum.Parse<JobStatus>(row.Status), row.MaxDepth, row.MaxPages,
                row.CreatedAt, row.StartedAt, row.CompletedAt, row.FailureReason,
                new JobProgress(row.PagesDiscovered, row.PagesCompleted, row.PagesFailed, row.PagesDuplicate));
    }

    public async Task<IReadOnlyList<PageRecord>> GetPagesAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<PageRow>(new CommandDefinition("""
            SELECT p.id, p.parent_page_id, p.url, p.depth, p.status, p.http_status, p.error,
                   p.domain_link_ratio, p.outgoing_link_count, p.duplicate_of_page_id, original.url AS duplicate_of_url
              FROM pages p
              LEFT JOIN pages original ON original.id = p.duplicate_of_page_id
             WHERE p.job_id = @jobId
            """,
            new { jobId }, cancellationToken: cancellationToken));

        return rows
            .Select(r => new PageRecord(r.Id, r.ParentPageId, r.Url, r.Depth, Enum.Parse<PageStatus>(r.Status),
                r.HttpStatus, r.Error, (double?)r.DomainLinkRatio, r.OutgoingLinkCount,
                r.DuplicateOfPageId, r.DuplicateOfUrl))
            .ToList();
    }

    public async Task<PagedResult<JobListItem>> ListJobsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        using var results = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT id, url, status, created_at, started_at, completed_at, pages_discovered
              FROM crawl_jobs
             ORDER BY created_at DESC, id DESC
             LIMIT @pageSize OFFSET @offset;

            SELECT count(*) FROM crawl_jobs;
            """,
            new { pageSize, offset = (page - 1) * pageSize }, cancellationToken: cancellationToken));

        var items = (await results.ReadAsync<JobListRow>())
            .Select(r => new JobListItem(r.Id, r.Url, Enum.Parse<JobStatus>(r.Status), r.CreatedAt, r.StartedAt,
                r.CompletedAt, r.PagesDiscovered))
            .ToList();
        var total = await results.ReadSingleAsync<long>();

        return new PagedResult<JobListItem>(items, page, pageSize, total);
    }

    private sealed class JobRow
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = "";
        public string Status { get; set; } = "";
        public int MaxDepth { get; set; }
        public int MaxPages { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? FailureReason { get; set; }
        public int PagesDiscovered { get; set; }
        public int PagesCompleted { get; set; }
        public int PagesFailed { get; set; }
        public int PagesDuplicate { get; set; }
    }

    private sealed class JobListRow
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int PagesDiscovered { get; set; }
    }

    private sealed class PageRow
    {
        public Guid Id { get; set; }
        public Guid? ParentPageId { get; set; }
        public string Url { get; set; } = "";
        public int Depth { get; set; }
        public string Status { get; set; } = "";
        public int? HttpStatus { get; set; }
        public string? Error { get; set; }
        public decimal? DomainLinkRatio { get; set; }
        public int? OutgoingLinkCount { get; set; }
        public Guid? DuplicateOfPageId { get; set; }
        public string? DuplicateOfUrl { get; set; }
    }
}
