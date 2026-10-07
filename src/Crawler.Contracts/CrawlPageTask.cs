namespace Crawler.Contracts;

/// <summary>
/// "Crawl this page" work item. One message per page; the page row already exists
/// (it was claimed when discovered), so <see cref="PageId"/> is the idempotency key.
/// The other fields are informational (logging, routing); the database is the source of truth.
/// </summary>
public sealed record CrawlPageTask(
    int SchemaVersion,
    Guid MessageId,
    Guid JobId,
    Guid PageId,
    string Url,
    int Depth,
    int MaxDepth,
    string RootHost,
    Guid? ParentPageId,
    DateTime EnqueuedAt)
{
    public const int CurrentSchemaVersion = 1;
    public const string MessageType = "crawl.page-task.v1";
}
