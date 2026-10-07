using Crawler.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Crawler.IntegrationTests.Infrastructure;

/// <summary>One throwaway PostgreSQL container per test run, migrated with the real scripts.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());

        var migrator = new DatabaseMigrator(DataSource, NullLogger<DatabaseMigrator>.Instance);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // second run must be a no-op
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
