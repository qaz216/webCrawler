using System.Text.Json;
using Crawler.Application;
using Crawler.Infrastructure.Messaging;

namespace Crawler.IntegrationTests.Messaging;

public class FailurePolicyTests
{
    private static readonly TimeSpan[] Tiers = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    public void Transient_failures_walk_through_the_delay_tiers(int attempt, int expectedDelaySeconds)
    {
        var decision = FailurePolicy.Decide(new TransientCrawlException("HTTP 503"), attempt, Tiers);

        Assert.Equal(FailureAction.Retry, decision.Action);
        Assert.Equal(TimeSpan.FromSeconds(expectedDelaySeconds), decision.Delay);
    }

    [Fact]
    public void Transient_failure_after_the_last_tier_is_dead_lettered()
    {
        var decision = FailurePolicy.Decide(new TransientCrawlException("HTTP 503"), attempt: 4, Tiers);

        Assert.Equal(FailureAction.DeadLetter, decision.Action);
        Assert.Contains("Retries exhausted", decision.Reason);
    }

    [Fact]
    public void Poison_messages_are_dead_lettered_immediately()
    {
        Assert.Equal(FailureAction.DeadLetter, FailurePolicy.Decide(new PoisonMessageException("bad"), 1, Tiers).Action);
        Assert.Equal(FailureAction.DeadLetter, FailurePolicy.Decide(new JsonException("bad json"), 1, Tiers).Action);
    }

    [Fact]
    public void Unexpected_errors_get_a_single_retry()
    {
        Assert.Equal(FailureAction.Retry, FailurePolicy.Decide(new InvalidOperationException("bug"), 1, Tiers).Action);
        Assert.Equal(FailureAction.DeadLetter, FailurePolicy.Decide(new InvalidOperationException("bug"), 2, Tiers).Action);
    }

    [Fact]
    public void Transient_classification()
    {
        Assert.True(FailurePolicy.IsTransient(new TransientCrawlException("x")));
        Assert.True(FailurePolicy.IsTransient(new TimeoutException()));
        Assert.False(FailurePolicy.IsTransient(new InvalidOperationException()));
        Assert.False(FailurePolicy.IsTransient(new PoisonMessageException("x")));
    }
}
