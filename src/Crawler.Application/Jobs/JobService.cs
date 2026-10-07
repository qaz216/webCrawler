using Crawler.Application.Abstractions;
using Crawler.Domain.Urls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crawler.Application.Jobs;

/// <summary>Validation errors keyed by request field (rendered as RFC 7807 validation problem details).</summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Job commands: create (validate, normalize, persist job + root task atomically) and cancel.</summary>
public sealed class JobService(ICrawlStore store, IOptions<CrawlerOptions> options, ILogger<JobService> logger)
{
    public async Task<CreatedJob> CreateAsync(string? url, int? maxDepth, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var errors = new Dictionary<string, string[]>();

        var normalizedUrl = UrlNormalizer.Normalize(url);
        if (normalizedUrl is null)
            errors["url"] = ["Must be an absolute http:// or https:// URL."];

        var depth = maxDepth ?? settings.DefaultMaxDepth;
        if (depth < 0 || depth > settings.MaxAllowedDepth)
            errors["maxDepth"] = [$"Must be between 0 and {settings.MaxAllowedDepth}."];

        if (errors.Count > 0)
            throw new RequestValidationException(errors);

        var rootHost = new Uri(normalizedUrl!).Host;
        var job = await store.CreateJobAsync(
            new NewJob(normalizedUrl!, rootHost, depth, settings.MaxPagesPerJob), cancellationToken);

        using (logger.BeginScope(new Dictionary<string, object> { ["JobId"] = job.JobId }))
        {
            logger.LogInformation("Created crawl job for {Url} (max depth {MaxDepth}, max pages {MaxPages})",
                normalizedUrl, depth, settings.MaxPagesPerJob);
        }

        return job;
    }

    public async Task<CancelOutcome> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var outcome = await store.CancelJobAsync(jobId, cancellationToken);
        if (outcome == CancelOutcome.Canceled)
            logger.LogInformation("Canceled job {JobId}", jobId);
        return outcome;
    }
}
