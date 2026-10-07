using System.Data;
using Crawler.Application.Abstractions;
using Crawler.Contracts;
using Crawler.Domain.Jobs;
using Dapper;
using Npgsql;

namespace Crawler.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL implementation of <see cref="ICrawlStore"/>. Idempotency comes from the database, not memory:
/// unique keys + ON CONFLICT DO NOTHING, conditional status transitions, and a row lock on the job
/// while child pages are claimed so the page cap holds across concurrent workers.
/// </summary>
public sealed class CrawlStore(NpgsqlDataSource dataSource) : ICrawlStore
{
    static CrawlStore() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    public async Task<CreatedJob> CreateJobAsync(NewJob job, CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid();
        var rootPageId = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO crawl_jobs (id, url, root_host, max_depth, max_pages, status, pages_discovered)
            VALUES (@jobId, @url, @rootHost, @maxDepth, @maxPages, 'Pending', 1);

            INSERT INTO pages (id, job_id, url, depth, parent_page_id, status)
            VALUES (@rootPageId, @jobId, @url, 0, NULL, 'Pending');
            """,
            new { jobId, rootPageId, url = job.Url, rootHost = job.RootHost, maxDepth = job.MaxDepth, maxPages = job.MaxPages },
            transaction, cancellationToken: cancellationToken));

        var rootTask = NewTask(jobId, rootPageId, job.Url, depth: 0, job.MaxDepth, job.RootHost, parentPageId: null);
        await InsertOutboxAsync(connection, transaction, jobId, [rootTask], cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CreatedJob(jobId, rootPageId);
    }

    public async Task<LeaseResult> TryLeasePageAsync(Guid pageId, TimeSpan lease, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var leased = await connection.QuerySingleOrDefaultAsync<LeasedPageRow>(new CommandDefinition("""
            UPDATE pages p
               SET status = 'Processing', lease_until = now() + @lease, attempts = p.attempts + 1
              FROM crawl_jobs j
             WHERE p.id = @pageId
               AND j.id = p.job_id
               AND j.status IN ('Pending', 'Running')
               AND (p.status = 'Pending' OR (p.status = 'Processing' AND p.lease_until < now()))
            RETURNING p.id AS page_id, p.job_id, p.url, p.depth, j.max_depth, j.root_host
            """,
            new { pageId, lease }, cancellationToken: cancellationToken));

        if (leased is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE crawl_jobs SET status = 'Running', started_at = now()
                 WHERE id = @jobId AND status = 'Pending'
                """,
                new { jobId = leased.JobId }, cancellationToken: cancellationToken));

            return new LeaseResult(LeaseOutcome.Leased,
                new LeasedPage(leased.PageId, leased.JobId, leased.Url, leased.Depth, leased.MaxDepth, leased.RootHost));
        }

        // Not leased: work out why, so the caller can ack, retry or dead-letter.
        var state = await connection.QuerySingleOrDefaultAsync<PageStateRow>(new CommandDefinition("""
            SELECT p.status AS page_status, j.status AS job_status
              FROM pages p JOIN crawl_jobs j ON j.id = p.job_id
             WHERE p.id = @pageId
            """,
            new { pageId }, cancellationToken: cancellationToken));

        return new LeaseResult(state switch
        {
            null => LeaseOutcome.NotFound,
            _ when !Enum.Parse<JobStatus>(state.JobStatus).IsActive() => LeaseOutcome.JobNotActive,
            _ when Enum.Parse<PageStatus>(state.PageStatus).IsTerminal() => LeaseOutcome.AlreadyFinished,
            _ => LeaseOutcome.LeasedElsewhere,
        });
    }

    public async Task ReleasePageAsync(Guid pageId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE pages SET status = 'Pending', lease_until = NULL
             WHERE id = @pageId AND status = 'Processing'
            """,
            new { pageId }, cancellationToken: cancellationToken));
    }

    public async Task<PageCompletion> CompletePageAsync(PageOutcome outcome, CancellationToken cancellationToken)
    {
        if (!outcome.Status.IsTerminal() || outcome.Status == PageStatus.Duplicate)
            throw new ArgumentException($"Outcome status must be terminal, was {outcome.Status}.", nameof(outcome));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        CommandDefinition Command(string sql, object? parameters) =>
            new(sql, parameters, transaction, cancellationToken: cancellationToken);

        // 1. Lock the page's job row. All completions of a job are serialized from here on, so the
        //    page cap and "first page with this content wins" hold across concurrent workers.
        var job = await connection.QuerySingleOrDefaultAsync<JobRow>(Command("""
            SELECT j.id, j.status, j.max_depth, j.max_pages, j.pages_discovered, j.root_host
              FROM crawl_jobs j JOIN pages p ON p.job_id = j.id
             WHERE p.id = @pageId
               FOR UPDATE OF j
            """,
            new { pageId = outcome.PageId }));

        if (job is null)
            return PageCompletion.AlreadyFinished; // unknown page: nothing to record

        // 2. Content de-duplication: an earlier page of this job with identical HTML is the original.
        Guid? originalPageId = null;
        if (outcome.Status == PageStatus.Completed && outcome.ContentHash is not null)
        {
            originalPageId = await connection.QuerySingleOrDefaultAsync<Guid?>(Command("""
                SELECT id FROM pages
                 WHERE job_id = @jobId AND content_hash = @hash AND status = 'Completed' AND id <> @pageId
                """,
                new { jobId = job.Id, hash = outcome.ContentHash, pageId = outcome.PageId }));
        }

        var isDuplicate = originalPageId is not null;
        if (isDuplicate)
            outcome = outcome with { Links = [], ChildUrls = [] }; // the original already holds these

        // 3. Move the page to its final state, only once. A duplicate delivery stops here.
        var page = await connection.QuerySingleOrDefaultAsync<FinishedPageRow>(Command("""
            UPDATE pages
               SET status = @status, http_status = @httpStatus, content_type = @contentType, error = @error,
                   domain_link_ratio = @ratio, outgoing_link_count = @linkCount,
                   content_hash = @hash, duplicate_of_page_id = @originalPageId,
                   lease_until = NULL, finished_at = now()
             WHERE id = @pageId AND status IN ('Pending', 'Processing')
            RETURNING job_id, depth, parent_page_id
            """,
            new
            {
                pageId = outcome.PageId,
                status = (isDuplicate ? PageStatus.Duplicate : outcome.Status).ToString(),
                httpStatus = outcome.HttpStatus,
                contentType = outcome.ContentType,
                error = outcome.Error,
                ratio = isDuplicate ? null : outcome.DomainLinkRatio,
                linkCount = outcome.Status == PageStatus.Completed && !isDuplicate ? outcome.Links.Count : (int?)null,
                hash = outcome.Status == PageStatus.Completed ? outcome.ContentHash : null,
                originalPageId,
            }));

        if (page is null)
            return PageCompletion.AlreadyFinished;

        // 4. Edges (all outgoing links, internal and external).
        if (outcome.Links.Count > 0)
        {
            await connection.ExecuteAsync(Command("""
                INSERT INTO page_links (job_id, from_page_id, to_url, is_internal)
                SELECT @jobId, @pageId, t.url, t.is_internal
                  FROM unnest(@urls, @isInternal) AS t(url, is_internal)
                ON CONFLICT (from_page_id, to_url) DO NOTHING
                """,
                new
                {
                    jobId = page.JobId,
                    pageId = outcome.PageId,
                    urls = outcome.Links.Select(l => l.Url).ToArray(),
                    isInternal = outcome.Links.Select(l => l.IsInternal).ToArray(),
                }));
        }

        // 5. Claim unseen child URLs, up to the remaining page budget, and queue a task for each.
        var remaining = job.MaxPages - job.PagesDiscovered;
        if (Enum.Parse<JobStatus>(job.Status).IsActive() && remaining > 0 && outcome.ChildUrls.Count > 0)
        {
            var claimed = (await connection.QueryAsync<ClaimedPageRow>(Command("""
                INSERT INTO pages (id, job_id, url, depth, parent_page_id, status)
                SELECT gen_random_uuid(), @jobId, c.url, @depth, @parentPageId, 'Pending'
                  FROM unnest(@urls) WITH ORDINALITY AS c(url, ord)
                 WHERE NOT EXISTS (SELECT 1 FROM pages p WHERE p.job_id = @jobId AND p.url = c.url)
                 ORDER BY c.ord
                 LIMIT @remaining
                ON CONFLICT (job_id, url) DO NOTHING
                RETURNING id, url
                """,
                new
                {
                    jobId = page.JobId,
                    depth = page.Depth + 1,
                    parentPageId = outcome.PageId,
                    urls = outcome.ChildUrls.ToArray(),
                    remaining,
                }))).ToList();

            if (claimed.Count > 0)
            {
                await connection.ExecuteAsync(Command(
                    "UPDATE crawl_jobs SET pages_discovered = pages_discovered + @count WHERE id = @jobId",
                    new { count = claimed.Count, jobId = page.JobId }));

                var tasks = claimed
                    .Select(c => NewTask(page.JobId, c.Id, c.Url, page.Depth + 1, job.MaxDepth, job.RootHost, outcome.PageId))
                    .ToList();
                await InsertOutboxAsync(connection, transaction, page.JobId, tasks, cancellationToken);
            }
        }

        // 6. Point internal edges at the page rows they lead to (new or previously discovered).
        if (outcome.Links.Any(l => l.IsInternal))
        {
            await connection.ExecuteAsync(Command("""
                UPDATE page_links l SET to_page_id = p.id
                  FROM pages p
                 WHERE l.from_page_id = @pageId AND l.is_internal
                   AND p.job_id = @jobId AND p.url = l.to_url
                """,
                new { pageId = outcome.PageId, jobId = page.JobId }));
        }

        // 7. Counters, then job completion (conditional, so it happens exactly once).
        var failed = outcome.Status == PageStatus.Failed;
        await connection.ExecuteAsync(Command("""
            UPDATE crawl_jobs
               SET pages_completed = pages_completed + @completed,
                   pages_failed    = pages_failed + @failed,
                   pages_duplicate = pages_duplicate + @duplicate
             WHERE id = @jobId
            """,
            new
            {
                jobId = page.JobId,
                completed = !failed && !isDuplicate ? 1 : 0,
                failed = failed ? 1 : 0,
                duplicate = isDuplicate ? 1 : 0,
            }));

        if (failed && page.ParentPageId is null)
        {
            await connection.ExecuteAsync(Command("""
                UPDATE crawl_jobs
                   SET status = 'Failed', failure_reason = @reason,
                       started_at = coalesce(started_at, now()), completed_at = now()
                 WHERE id = @jobId AND status IN ('Pending', 'Running')
                """,
                new { jobId = page.JobId, reason = $"Start page failed: {outcome.Error}" }));
        }
        else
        {
            await connection.ExecuteAsync(Command("""
                UPDATE crawl_jobs
                   SET status = 'Completed', started_at = coalesce(started_at, now()), completed_at = now()
                 WHERE id = @jobId AND status IN ('Pending', 'Running')
                   AND pages_completed + pages_failed + pages_duplicate >= pages_discovered
                """,
                new { jobId = page.JobId }));
        }

        await transaction.CommitAsync(cancellationToken);
        return isDuplicate ? PageCompletion.RecordedAsDuplicate : PageCompletion.Recorded;
    }

    public async Task<CancelOutcome> CancelJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var canceled = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE crawl_jobs SET status = 'Canceled', completed_at = now()
             WHERE id = @jobId AND status IN ('Pending', 'Running')
            """,
            new { jobId }, cancellationToken: cancellationToken));

        if (canceled == 1)
            return CancelOutcome.Canceled;

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM crawl_jobs WHERE id = @jobId)",
            new { jobId }, cancellationToken: cancellationToken));

        return exists ? CancelOutcome.NotActive : CancelOutcome.NotFound;
    }

    private static CrawlPageTask NewTask(
        Guid jobId, Guid pageId, string url, int depth, int maxDepth, string rootHost, Guid? parentPageId) =>
        new(CrawlPageTask.CurrentSchemaVersion, MessageId: pageId, jobId, pageId, url, depth, maxDepth, rootHost,
            parentPageId, DateTime.UtcNow);

    private static Task InsertOutboxAsync(
        NpgsqlConnection connection, IDbTransaction transaction, Guid jobId,
        IReadOnlyList<CrawlPageTask> tasks, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO outbox_messages (id, message_type, payload, correlation_id)
            SELECT t.id, @messageType, t.payload::jsonb, @jobId
              FROM unnest(@ids, @payloads) AS t(id, payload)
            ON CONFLICT (id) DO NOTHING
            """,
            new
            {
                messageType = CrawlPageTask.MessageType,
                jobId,
                ids = tasks.Select(t => t.MessageId).ToArray(),
                payloads = tasks.Select(MessageJson.Serialize).ToArray(),
            },
            transaction, cancellationToken: cancellationToken));

    private sealed class LeasedPageRow
    {
        public Guid PageId { get; set; }
        public Guid JobId { get; set; }
        public string Url { get; set; } = "";
        public int Depth { get; set; }
        public int MaxDepth { get; set; }
        public string RootHost { get; set; } = "";
    }

    private sealed class PageStateRow
    {
        public string PageStatus { get; set; } = "";
        public string JobStatus { get; set; } = "";
    }

    private sealed class FinishedPageRow
    {
        public Guid JobId { get; set; }
        public int Depth { get; set; }
        public Guid? ParentPageId { get; set; }
    }

    private sealed class JobRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public int MaxDepth { get; set; }
        public int MaxPages { get; set; }
        public int PagesDiscovered { get; set; }
        public string RootHost { get; set; } = "";
    }

    private sealed class ClaimedPageRow
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = "";
    }
}
