namespace Crawler.Application;

/// <summary>
/// A failure worth retrying later (timeouts, 5xx/408/429, connection errors, page leased elsewhere).
/// The consumer re-queues the message with a delay; after the last retry it is dead-lettered.
/// </summary>
public class TransientCrawlException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A message that can never succeed (unknown schema, missing page). Dead-lettered without retries.
/// </summary>
public sealed class PoisonMessageException(string message) : Exception(message);
