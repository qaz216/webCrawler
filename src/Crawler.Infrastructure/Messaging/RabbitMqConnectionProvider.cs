using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Crawler.Infrastructure.Messaging;

/// <summary>
/// One shared AMQP connection per process, opened lazily with retries (the broker may still be
/// starting). After that, the client's automatic recovery restores the connection and consumers.
/// </summary>
public sealed class RabbitMqConnectionProvider(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnectionProvider> logger) : IAsyncDisposable
{
    private const int MaxConnectAttempts = 15;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsConnected => _connection is { IsOpen: true };

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
            return _connection;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _connection ??= await ConnectAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            Uri = new Uri(settings.Uri),
            ClientProvidedName = settings.ClientName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ConsumerDispatchConcurrency = settings.ConsumerConcurrency,
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var connection = await factory.CreateConnectionAsync(cancellationToken);
                logger.LogInformation("Connected to RabbitMQ at {Host}", factory.HostName);
                return connection;
            }
            catch (BrokerUnreachableException ex) when (attempt < MaxConnectAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
                logger.LogWarning(ex, "RabbitMQ not reachable (attempt {Attempt}), retrying in {Delay}", attempt, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _lock.Dispose();
    }
}
