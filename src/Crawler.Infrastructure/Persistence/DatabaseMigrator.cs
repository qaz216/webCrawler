using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Crawler.Infrastructure.Persistence;

/// <summary>
/// Applies the embedded SQL scripts in Persistence/Migrations in name order, once each.
/// Runs in a single transaction under an advisory lock, so concurrent starts are safe.
/// </summary>
public sealed class DatabaseMigrator(NpgsqlDataSource dataSource, ILogger<DatabaseMigrator> logger)
{
    private const long AdvisoryLockKey = 0x5745_4243_5241_574C; // "WEBCRAWL"
    private const string ResourcePrefix = "Crawler.Infrastructure.Persistence.Migrations.";

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(Command("SELECT pg_advisory_xact_lock(@key)", new { key = AdvisoryLockKey }));
        await connection.ExecuteAsync(Command("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id         text        PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            )
            """));

        var applied = (await connection.QueryAsync<string>(Command("SELECT id FROM schema_migrations"))).ToHashSet();

        foreach (var (id, sql) in LoadScripts().Where(script => !applied.Contains(script.Id)))
        {
            logger.LogInformation("Applying migration {MigrationId}", id);
            await connection.ExecuteAsync(Command(sql));
            await connection.ExecuteAsync(Command("INSERT INTO schema_migrations (id) VALUES (@id)", new { id }));
        }

        await transaction.CommitAsync(cancellationToken);

        CommandDefinition Command(string sql, object? parameters = null) =>
            new(sql, parameters, transaction, cancellationToken: cancellationToken);
    }

    private static IEnumerable<(string Id, string Sql)> LoadScripts()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => (name[ResourcePrefix.Length..^".sql".Length], ReadResource(assembly, name)));
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
