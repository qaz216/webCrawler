using System.Text;
using Crawler.Application;
using Crawler.Application.Abstractions;
using Crawler.Application.Pages;
using Crawler.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// Consumes <see cref="CrawlPageTask"/> messages from <c>crawl.pages</c> with manual acks.
/// Every delivery ends in exactly one of: ack (done or duplicate), re-publish to a retry tier then ack,
/// or publish to the DLQ then ack. The ack always comes after the follow-up publish is confirmed,
/// so a crash in between causes a redelivery (handled idempotently), never a lost message.
/// </summary>
public sealed class PageTaskConsumer(
    RabbitMqConnectionProvider connections,
    MessagePublisher publisher,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<PageTaskConsumer> logger) : BackgroundService
{
    public const string AttemptHeader = "x-attempt";
    public const string ErrorHeader = "x-error";
    public const string ErrorTypeHeader = "x-error-type";

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var connection = await connections.GetConnectionAsync(stoppingToken);

        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(_channel, settings.EffectiveRetryDelays, stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: settings.Prefetch, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(delivery, stoppingToken);

        await _channel.BasicConsumeAsync(RabbitMqTopology.PagesQueue, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consuming {Queue} (prefetch {Prefetch}, concurrency {Concurrency})",
            RabbitMqTopology.PagesQueue, settings.Prefetch, settings.ConsumerConcurrency);

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private async Task OnReceivedAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var attempt = ReadAttempt(delivery.BasicProperties);
        var body = delivery.Body.ToArray(); // the delivery buffer is only valid during this callback
        CrawlPageTask? task = null;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MessageId"] = delivery.BasicProperties.MessageId,
            ["CorrelationId"] = delivery.BasicProperties.CorrelationId,
            ["Attempt"] = attempt,
        });

        try
        {
            task = Deserialize(body);

            using var taskScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["JobId"] = task.JobId,
                ["PageId"] = task.PageId,
            });

            await using var services = scopeFactory.CreateAsyncScope();
            await services.ServiceProvider.GetRequiredService<PageTaskHandler>().HandleAsync(task, stoppingToken);

            await AckAsync(delivery);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down mid-message: hand it back to the broker for another consumer.
            await NackAsync(delivery);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(delivery, body, task, attempt, ex);
        }
    }

    private async Task HandleFailureAsync(
        BasicDeliverEventArgs delivery, byte[] body, CrawlPageTask? task, int attempt, Exception exception)
    {
        var decision = FailurePolicy.Decide(exception, attempt, options.Value.EffectiveRetryDelays);

        try
        {
            if (decision.Action == FailureAction.Retry)
            {
                logger.LogWarning(exception, "Attempt {Attempt} failed; retrying in {Delay}", attempt, decision.Delay);
                await publisher.PublishAsync("", RabbitMqTopology.RetryQueueName(decision.Delay), body,
                    CopyProperties(delivery.BasicProperties, attempt + 1), CancellationToken.None);
            }
            else
            {
                logger.LogError(exception, "Dead-lettering message: {Reason}", decision.Reason);
                await publisher.PublishAsync(RabbitMqTopology.DeadLetterExchange, RabbitMqTopology.RoutingKey, body,
                    DeadLetterProperties(delivery.BasicProperties, attempt, exception, decision.Reason), CancellationToken.None);

                if (task is not null)
                    await MarkPageFailedAsync(task.PageId, decision.Reason);
            }

            await AckAsync(delivery);
        }
        catch (Exception publishFailure)
        {
            // Could not hand the message on (broker trouble): let RabbitMQ redeliver it.
            // x-delivery-limit dead-letters it broker-side if this keeps happening.
            logger.LogError(publishFailure, "Failed to re-route message after failure; requeueing");
            await NackAsync(delivery);
        }
    }

    /// <summary>A dead-lettered page counts as failed, so its job can still complete.</summary>
    private async Task MarkPageFailedAsync(Guid pageId, string reason)
    {
        try
        {
            await using var services = scopeFactory.CreateAsyncScope();
            var store = services.ServiceProvider.GetRequiredService<ICrawlStore>();
            await store.CompletePageAsync(PageOutcome.Failed(pageId, $"Dead-lettered: {reason}"), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not mark dead-lettered page {PageId} as failed", pageId);
        }
    }

    private static CrawlPageTask Deserialize(byte[] body)
    {
        var task = MessageJson.Deserialize<CrawlPageTask>(Encoding.UTF8.GetString(body));
        if (task is null || task.PageId == Guid.Empty || task.JobId == Guid.Empty)
            throw new PoisonMessageException("Message body is not a valid CrawlPageTask.");
        return task;
    }

    private static int ReadAttempt(IReadOnlyBasicProperties properties) =>
        (properties.Headers is { } headers && headers.TryGetValue(AttemptHeader, out var raw) ? raw : null) switch
        {
            int value => value,
            long value => (int)value,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var value) => value,
            _ => 1,
        };

    private static BasicProperties CopyProperties(IReadOnlyBasicProperties source, int attempt)
    {
        var headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source.Headers);
        headers[AttemptHeader] = attempt;

        return new BasicProperties
        {
            Persistent = true,
            ContentType = source.ContentType,
            Type = source.Type,
            MessageId = source.MessageId,
            CorrelationId = source.CorrelationId,
            Headers = headers,
        };
    }

    private static BasicProperties DeadLetterProperties(
        IReadOnlyBasicProperties source, int attempt, Exception exception, string reason)
    {
        var properties = CopyProperties(source, attempt);
        properties.Headers![ErrorHeader] = Truncate(reason, 1000);
        properties.Headers[ErrorTypeHeader] = exception.GetType().FullName;
        properties.Headers["x-dead-lettered-at"] = DateTimeOffset.UtcNow.ToString("O");
        return properties;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private async Task AckAsync(BasicDeliverEventArgs delivery) =>
        await _channel!.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);

    private async Task NackAsync(BasicDeliverEventArgs delivery)
    {
        try
        {
            await _channel!.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Channel gone: the broker requeues unacked messages on its own.
            logger.LogWarning(ex, "Could not nack delivery {DeliveryTag}", delivery.DeliveryTag);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
            await _channel.DisposeAsync();
    }
}
