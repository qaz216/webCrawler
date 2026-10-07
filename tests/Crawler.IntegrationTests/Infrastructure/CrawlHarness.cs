using Crawler.Application;
using Crawler.Application.Abstractions;
using Crawler.Application.Pages;
using Crawler.Contracts;
using Crawler.Infrastructure.Http;
using Crawler.Infrastructure.Outbox;
using Crawler.Infrastructure.Persistence;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Crawler.IntegrationTests.Infrastructure;

/// <summary>
/// Wires the real handler, store, outbox and fetcher together and stands in for the broker:
/// drains the outbox, delivers tasks, re-queues on transient failure and "dead-letters"
/// (marks the page failed) after <see cref="MaxAttempts"/>, mirroring the consumer's policy.
/// </summary>
public sealed class CrawlHarness
{
    public const int MaxAttempts = 3;

    private readonly NpgsqlDataSource _dataSource;

    public CrawlHarness(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        Site = new FixtureSiteHandler();
        Store = new CrawlStore(dataSource);
        Outbox = new OutboxStore(dataSource);
        Handler = new PageTaskHandler(
            Store,
            new HttpPageFetcher(new HttpClient(Site) { Timeout = TimeSpan.FromSeconds(10) }),
            Options.Create(new CrawlerOptions()),
            NullLogger<PageTaskHandler>.Instance);
    }

    public FixtureSiteHandler Site { get; }

    public CrawlStore Store { get; }

    public OutboxStore Outbox { get; }

    public PageTaskHandler Handler { get; }

    public Task<CreatedJob> StartJobAsync(string path = "/", int maxDepth = 2, int maxPages = 200)
    {
        var url = new Uri(FixtureSiteHandler.Root, path).AbsoluteUri;
        return Store.CreateJobAsync(new NewJob(url, FixtureSiteHandler.Host, maxDepth, maxPages), CancellationToken.None);
    }

    /// <summary>Takes the pending tasks for a job out of the outbox (as the dispatcher would).</summary>
    public async Task<IReadOnlyList<CrawlPageTask>> DequeueTasksAsync(Guid jobId)
    {
        var messages = (await Outbox.GetUnsentAsync(1000, CancellationToken.None))
            .Where(m => m.CorrelationId == jobId)
            .ToList();

        await Outbox.MarkSentAsync(messages.Select(m => m.Id).ToList(), CancellationToken.None);
        return messages.Select(m => MessageJson.Deserialize<CrawlPageTask>(m.Payload)!).ToList();
    }

    /// <summary>Delivers tasks until the job's outbox is empty.</summary>
    public async Task RunToCompletionAsync(Guid jobId)
    {
        var attempts = new Dictionary<Guid, int>();
        var queue = new Queue<CrawlPageTask>();

        while (true)
        {
            foreach (var task in await DequeueTasksAsync(jobId))
                queue.Enqueue(task);

            if (queue.Count == 0)
                return;

            while (queue.TryDequeue(out var task))
            {
                try
                {
                    await Handler.HandleAsync(task, CancellationToken.None);
                }
                catch (TransientCrawlException)
                {
                    attempts[task.PageId] = attempts.GetValueOrDefault(task.PageId) + 1;
                    if (attempts[task.PageId] < MaxAttempts)
                        queue.Enqueue(task);
                    else
                        await Store.CompletePageAsync(PageOutcome.Failed(task.PageId, "dead-lettered"), CancellationToken.None);
                }
            }
        }
    }

    public async Task<JobRow> GetJobAsync(Guid jobId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<JobRow>(
            "SELECT * FROM crawl_jobs WHERE id = @jobId", new { jobId });
    }

    public async Task<Dictionary<string, PageRow>> GetPagesAsync(Guid jobId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        var pages = await connection.QueryAsync<PageRow>("""
            SELECT p.url, p.depth, p.status, p.http_status, p.domain_link_ratio, p.outgoing_link_count,
                   p.attempts, parent.url AS parent_url, original.url AS duplicate_of_url,
                   (SELECT count(*) FROM pages c WHERE c.parent_page_id = p.id) AS child_count
              FROM pages p
              LEFT JOIN pages parent ON parent.id = p.parent_page_id
              LEFT JOIN pages original ON original.id = p.duplicate_of_page_id
             WHERE p.job_id = @jobId
            """, new { jobId });

        return pages.ToDictionary(p => new Uri(p.Url).PathAndQuery);
    }

    public async Task<(long Pages, long Links, long Outbox)> CountRowsAsync(Guid jobId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<(long, long, long)>("""
            SELECT (SELECT count(*) FROM pages WHERE job_id = @jobId),
                   (SELECT count(*) FROM page_links WHERE job_id = @jobId),
                   (SELECT count(*) FROM outbox_messages WHERE correlation_id = @jobId)
            """, new { jobId });
    }

    public async Task SetJobStatusAsync(Guid jobId, string status)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_jobs SET status = @status WHERE id = @jobId", new { jobId, status });
    }

    public sealed class JobRow
    {
        public string Status { get; set; } = "";
        public string? FailureReason { get; set; }
        public int PagesDiscovered { get; set; }
        public int PagesCompleted { get; set; }
        public int PagesFailed { get; set; }
        public int PagesDuplicate { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public sealed class PageRow
    {
        public string Url { get; set; } = "";
        public int Depth { get; set; }
        public string Status { get; set; } = "";
        public int? HttpStatus { get; set; }
        public decimal? DomainLinkRatio { get; set; }
        public int? OutgoingLinkCount { get; set; }
        public int Attempts { get; set; }
        public string? ParentUrl { get; set; }
        public string? DuplicateOfUrl { get; set; }
        public long ChildCount { get; set; }
    }
}
