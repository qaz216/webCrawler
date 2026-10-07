namespace Crawler.Domain.Jobs;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled,
}

public static class JobStatusExtensions
{
    public static bool IsActive(this JobStatus status) =>
        status is JobStatus.Pending or JobStatus.Running;
}
