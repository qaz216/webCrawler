using Testcontainers.RabbitMq;

namespace Crawler.IntegrationTests.Infrastructure;

/// <summary>Throwaway RabbitMQ broker (same image as docker-compose).</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3.13-management-alpine").Build();

    public string Uri => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Tests that run the real worker host against PostgreSQL + RabbitMQ.</summary>
[CollectionDefinition(Name)]
public sealed class MessagingCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "messaging";
}
