using Crawler.Application.Abstractions;
using Crawler.Application.Jobs;

namespace Crawler.Api.Endpoints;

public sealed record CreateJobRequest(string? Url, int? MaxDepth);

public sealed record CreateJobResponse(Guid JobId);

public sealed record JobTreeResponse(Guid JobId, string Status, PageTreeNode? Root);

public sealed record ClearJobsResponse(int Deleted);

public static class JobEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var jobs = app.MapGroup("/api/jobs").WithTags("Jobs");

        jobs.MapPost("/", CreateJob)
            .WithName("CreateJob")
            .WithSummary("Start a crawl job. Returns immediately; the crawl runs asynchronously.")
            .Produces<CreateJobResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        jobs.MapDelete("/", ClearJobs)
            .WithName("ClearJobs")
            .WithSummary("Delete all jobs and their results (history). Running jobs are stopped. Cannot be undone.")
            .Produces<ClearJobsResponse>();

        jobs.MapGet("/", ListJobs)
            .WithName("ListJobs")
            .WithSummary("Job history, most recent first.")
            .Produces<PagedResult<JobListItem>>()
            .ProducesValidationProblem();

        jobs.MapGet("/{jobId:guid}", GetJob)
            .WithName("GetJob")
            .WithSummary("Job status, timestamps and progress counters.")
            .Produces<JobDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapGet("/{jobId:guid}/tree", GetTree)
            .WithName("GetJobTree")
            .WithSummary("Hierarchical result: pages by first discoverer, with Domain Link Ratio. Partial while running.")
            .Produces<JobTreeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapGet("/{jobId:guid}/pages/{pageId:guid}", GetPageLinks)
            .WithName("GetPageLinks")
            .WithSummary("One crawled page with its outgoing links, split into in-domain and outbound.")
            .Produces<PageLinks>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapPost("/{jobId:guid}/cancel", CancelJob)
            .WithName("CancelJob")
            .WithSummary("Cancel a Pending or Running job.")
            .Produces<JobDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateJob(CreateJobRequest request, JobService jobs, CancellationToken cancellationToken)
    {
        var job = await jobs.CreateAsync(request.Url, request.MaxDepth, cancellationToken);
        return Results.AcceptedAtRoute("GetJob", new { jobId = job.JobId }, new CreateJobResponse(job.JobId));
    }

    private static async Task<IResult> ClearJobs(JobService jobs, CancellationToken cancellationToken) =>
        Results.Ok(new ClearJobsResponse(await jobs.ClearAllAsync(cancellationToken)));

    private static async Task<IResult> ListJobs(ICrawlQueries queries, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
            errors["page"] = ["Must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        return Results.Ok(await queries.ListJobsAsync(page, pageSize, cancellationToken));
    }

    private static async Task<IResult> GetJob(Guid jobId, ICrawlQueries queries, CancellationToken cancellationToken) =>
        await queries.GetJobAsync(jobId, cancellationToken) is { } job
            ? Results.Ok(job)
            : JobNotFound(jobId);

    private static async Task<IResult> GetTree(Guid jobId, ICrawlQueries queries, CancellationToken cancellationToken)
    {
        if (await queries.GetJobAsync(jobId, cancellationToken) is not { } job)
            return JobNotFound(jobId);

        var pages = await queries.GetPagesAsync(jobId, cancellationToken);
        return Results.Ok(new JobTreeResponse(jobId, job.Status.ToString(), PageTreeBuilder.Build(pages)));
    }

    private static async Task<IResult> GetPageLinks(
        Guid jobId, Guid pageId, ICrawlQueries queries, CancellationToken cancellationToken) =>
        await queries.GetPageLinksAsync(jobId, pageId, cancellationToken) is { } page
            ? Results.Ok(page)
            : Results.Problem(title: "Page not found", detail: $"No page {pageId} in crawl job {jobId}.",
                statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> CancelJob(
        Guid jobId, JobService jobs, ICrawlQueries queries, CancellationToken cancellationToken)
    {
        return await jobs.CancelAsync(jobId, cancellationToken) switch
        {
            CancelOutcome.Canceled => Results.Ok(await queries.GetJobAsync(jobId, cancellationToken)),
            CancelOutcome.NotFound => JobNotFound(jobId),
            _ => Results.Problem(
                title: "Job is not active",
                detail: $"Job {jobId} has already finished and cannot be canceled.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    private static IResult JobNotFound(Guid jobId) =>
        Results.Problem(title: "Job not found", detail: $"No crawl job with id {jobId}.", statusCode: StatusCodes.Status404NotFound);
}
