using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// Exchanges and queues (README → Messaging → Topology). Declarations are idempotent,
/// so the API and every worker declare them on startup.
/// <code>
///   crawl (direct) --page--> crawl.pages (quorum) --consumer--> worker
///   worker --retry--> crawl.pages.retry.{n}ms (TTL) --expires--> crawl / page
///   worker --dead-letter--> crawl.dlx (direct) --page--> crawl.pages.dlq
/// </code>
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "crawl";
    public const string RoutingKey = "page";
    public const string PagesQueue = "crawl.pages";
    public const string DeadLetterExchange = "crawl.dlx";
    public const string DeadLetterQueue = "crawl.pages.dlq";

    /// <summary>
    /// Broker-side safety net for crash loops: a message redelivered this many times
    /// (consumer died before acking) is dead-lettered by RabbitMQ itself.
    /// </summary>
    public const int DeliveryLimit = 10;

    /// <summary>The delay is in the name, so changing the tiers never conflicts with existing queue arguments.</summary>
    public static string RetryQueueName(TimeSpan delay) => $"crawl.pages.retry.{(long)delay.TotalMilliseconds}ms";

    public static async Task DeclareAsync(IChannel channel, IEnumerable<TimeSpan> retryDelays, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(PagesQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = DeliveryLimit,
                ["x-dead-letter-exchange"] = DeadLetterExchange,
                ["x-dead-letter-routing-key"] = RoutingKey,
            },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(PagesQueue, Exchange, RoutingKey, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, RoutingKey, cancellationToken: cancellationToken);

        foreach (var delay in retryDelays.Distinct())
        {
            // No consumers: messages wait out the TTL, then dead-letter back onto the main exchange.
            await channel.QueueDeclareAsync(RetryQueueName(delay), durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = (long)delay.TotalMilliseconds,
                    ["x-dead-letter-exchange"] = Exchange,
                    ["x-dead-letter-routing-key"] = RoutingKey,
                },
                cancellationToken: cancellationToken);
        }
    }
}
