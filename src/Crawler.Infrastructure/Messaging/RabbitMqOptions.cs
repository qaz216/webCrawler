namespace Crawler.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public static readonly TimeSpan[] DefaultRetryDelays =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    public string Uri { get; set; } = "amqp://guest:guest@localhost:5672/";

    public string ClientName { get; set; } = "webcrawler";

    /// <summary>Unacked messages a consumer may hold.</summary>
    public ushort Prefetch { get; set; } = 10;

    /// <summary>Messages processed in parallel per worker process.</summary>
    public ushort ConsumerConcurrency { get; set; } = 4;

    /// <summary>
    /// Delay tiers for message-level retries; one retry per tier, then the DLQ.
    /// Null means <see cref="DefaultRetryDelays"/> (config binding appends to non-null arrays).
    /// </summary>
    public TimeSpan[]? RetryDelays { get; set; }

    public IReadOnlyList<TimeSpan> EffectiveRetryDelays => RetryDelays is { Length: > 0 } delays ? delays : DefaultRetryDelays;

    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int OutboxBatchSize { get; set; } = 100;
}
