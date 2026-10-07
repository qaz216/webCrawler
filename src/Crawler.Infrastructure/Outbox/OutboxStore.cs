using Dapper;
using Npgsql;

namespace Crawler.Infrastructure.Outbox;

public sealed record OutboxMessage(Guid Id, string MessageType, string Payload, Guid? CorrelationId);

/// <summary>Reads pending outbox messages and marks them sent once the broker has confirmed them.</summary>
public sealed class OutboxStore(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<OutboxMessage>> GetUnsentAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<OutboxMessage>(new CommandDefinition("""
            SELECT id, message_type AS MessageType, payload::text AS Payload, correlation_id AS CorrelationId
              FROM outbox_messages
             WHERE sent_at IS NULL
             ORDER BY created_at
             LIMIT @batchSize
            """,
            new { batchSize }, cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task MarkSentAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE outbox_messages SET sent_at = now() WHERE id = ANY(@ids) AND sent_at IS NULL",
            new { ids = ids.ToArray() }, cancellationToken: cancellationToken));
    }
}
