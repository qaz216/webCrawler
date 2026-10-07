using System.Text;
using Crawler.Infrastructure.Outbox;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// Publishes with publisher confirms: <see cref="PublishAsync"/> completes only once the broker
/// has accepted (and, for durable queues, persisted) the message, and throws if it was nacked or unroutable.
/// </summary>
public sealed class MessagePublisher(
    RabbitMqConnectionProvider connections,
    IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public Task PublishTaskAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = message.MessageType,
            MessageId = message.Id.ToString(),
            CorrelationId = message.CorrelationId?.ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };

        return PublishAsync(RabbitMqTopology.Exchange, RabbitMqTopology.RoutingKey, Encoding.UTF8.GetBytes(message.Payload),
            properties, cancellationToken);
    }

    public async Task PublishAsync(
        string exchange, string routingKey, ReadOnlyMemory<byte> body, BasicProperties properties,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, body, cancellationToken);
        }
        catch
        {
            // A failed confirm may leave the channel closed; start fresh on the next publish.
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        var connection = await connections.GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, options.Value.EffectiveRetryDelays, cancellationToken);
        return _channel = channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is null)
            return;

        try
        {
            await _channel.DisposeAsync();
        }
        catch
        {
            // Already broken; nothing to clean up.
        }

        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _lock.Dispose();
    }
}
