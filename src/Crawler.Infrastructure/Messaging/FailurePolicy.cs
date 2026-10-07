using System.Text.Json;
using Crawler.Application;
using Npgsql;
using RabbitMQ.Client.Exceptions;

namespace Crawler.Infrastructure.Messaging;

public enum FailureAction
{
    Retry,
    DeadLetter,
}

public sealed record FailureDecision(FailureAction Action, string Reason, TimeSpan Delay = default);

/// <summary>
/// Decides what happens to a message whose handling threw (README → Retry policy / DLQ handling):
/// <list type="bullet">
/// <item>poison (bad payload, unknown page/schema) → dead-letter immediately;</item>
/// <item>transient (fetch timeouts/5xx/429, transient DB or broker errors) → retry through each delay tier, then dead-letter;</item>
/// <item>anything else (a bug) → one retry, then dead-letter, so a defect never hot-loops.</item>
/// </list>
/// </summary>
public static class FailurePolicy
{
    public const int UnexpectedErrorMaxRetries = 1;

    /// <param name="attempt">1 for the first delivery, incremented on every retry.</param>
    public static FailureDecision Decide(Exception exception, int attempt, IReadOnlyList<TimeSpan> retryDelays)
    {
        if (IsPoison(exception))
            return new FailureDecision(FailureAction.DeadLetter, $"Poison message: {exception.Message}");

        var maxRetries = IsTransient(exception) ? retryDelays.Count : Math.Min(UnexpectedErrorMaxRetries, retryDelays.Count);

        if (attempt <= maxRetries)
            return new FailureDecision(FailureAction.Retry, exception.Message, retryDelays[attempt - 1]);

        return new FailureDecision(FailureAction.DeadLetter, IsTransient(exception)
            ? $"Retries exhausted after {attempt} attempts: {exception.Message}"
            : $"Unexpected error after {attempt} attempts: {exception.Message}");
    }

    public static bool IsPoison(Exception exception) =>
        exception is PoisonMessageException or JsonException;

    public static bool IsTransient(Exception exception) => exception switch
    {
        TransientCrawlException => true,
        NpgsqlException npgsql => npgsql.IsTransient, // includes deadlocks / serialization failures
        TimeoutException => true,
        BrokerUnreachableException or AlreadyClosedException => true,
        _ => false,
    };
}
