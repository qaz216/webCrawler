using Crawler.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crawler.Infrastructure.Outbox;

/// <summary>Publishes outbox rows to RabbitMQ. Polls quickly while there is work, backs off when idle or failing.</summary>
public sealed class OutboxDispatcher(
    OutboxStore outbox,
    MessagePublisher publisher,
    IOptions<RabbitMqOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await outbox.DispatchBatchAsync(settings.OutboxBatchSize, publisher.PublishTaskAsync, stoppingToken);
                if (dispatched > 0)
                    logger.LogDebug("Dispatched {Count} outbox messages", dispatched);

                if (dispatched < settings.OutboxBatchSize)
                    await Task.Delay(settings.OutboxPollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox dispatch failed; retrying in {Delay}", ErrorBackoff);
                await Task.Delay(ErrorBackoff, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }
}
