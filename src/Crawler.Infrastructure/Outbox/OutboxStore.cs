using Dapper;
using Npgsql;

namespace Crawler.Infrastructure.Outbox;

public sealed record OutboxMessage(Guid Id, string MessageType, string Payload, Guid? CorrelationId);

/// <summary>Reads pending outbox messages and marks them sent once the broker has confirmed them.</summary>
public sealed class OutboxStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Locks up to <paramref name="batchSize"/> unsent messages (SKIP LOCKED, so several dispatchers
    /// can run side by side), publishes each one, then marks them sent in the same transaction.
    /// If publishing fails part-way, nothing is marked and the batch is retried — consumers are idempotent,
    /// so re-publishing the messages that did go out is harmless.
    /// </summary>
    public async Task<int> DispatchBatchAsync(
        int batchSize, Func<OutboxMessage, CancellationToken, Task> publish, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var messages = (await connection.QueryAsync<OutboxMessage>(new CommandDefinition("""
            SELECT id, message_type AS MessageType, payload::text AS Payload, correlation_id AS CorrelationId
              FROM outbox_messages
             WHERE sent_at IS NULL
             ORDER BY created_at
             LIMIT @batchSize
               FOR UPDATE SKIP LOCKED
            """,
            new { batchSize }, transaction, cancellationToken: cancellationToken))).ToList();

        if (messages.Count == 0)
            return 0;

        foreach (var message in messages)
            await publish(message, cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE outbox_messages SET sent_at = now() WHERE id = ANY(@ids)",
            new { ids = messages.Select(m => m.Id).ToArray() }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

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
