using System.Text;
using Crawler.Application.Abstractions;
using Crawler.Infrastructure;
using Crawler.Infrastructure.Http;
using Crawler.Infrastructure.Messaging;
using Crawler.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using RabbitMQ.Client;

namespace Crawler.IntegrationTests.Messaging;

/// <summary>
/// Runs the real worker composition (outbox dispatcher + RabbitMQ consumer + handler) against
/// PostgreSQL and RabbitMQ containers. Only the network is faked (fixture site), and retry delays
/// are shortened. In-process HTTP retries are off so every transient failure goes through the broker.
/// </summary>
[Collection(MessagingCollection.Name)]
public class WorkerEndToEndTests(PostgresFixture db, RabbitMqFixture broker)
{
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] FastRetryDelays =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(400)];

    [Fact]
    public async Task Crawls_fixture_site_through_rabbitmq()
    {
        var site = new FixtureSiteHandler();
        using var host = await StartWorkerAsync(site);
        var store = host.Services.GetRequiredService<ICrawlStore>();

        var job = await store.CreateJobAsync(NewJob("/"), CancellationToken.None);
        var summary = await WaitForJobAsync(job.JobId);

        Assert.Equal("Completed", summary.Status);
        Assert.Equal(13, summary.PagesDiscovered);
        Assert.Equal(9, summary.PagesCompleted);
        Assert.Equal(1, summary.PagesFailed);
        Assert.Equal(3, summary.PagesDuplicate);
        Assert.Equal(2, site.RequestCount("/flaky.html")); // 503 → retry tier → success
        Assert.Equal(0, site.RequestCount("/products/c.html"));
    }

    [Fact]
    public async Task Page_that_keeps_failing_is_retried_then_dead_lettered()
    {
        var site = new FixtureSiteHandler();
        using var host = await StartWorkerAsync(site);
        var store = host.Services.GetRequiredService<ICrawlStore>();

        var job = await store.CreateJobAsync(NewJob("/down.html"), CancellationToken.None);
        var summary = await WaitForJobAsync(job.JobId);

        // 1 delivery + 3 retry tiers, then the DLQ; the dead-lettered root page fails the job.
        Assert.Equal(4, site.RequestCount("/down.html"));
        Assert.Equal("Failed", summary.Status);
        Assert.Contains("Dead-lettered", summary.FailureReason);

        var dead = await WaitForDeadLetterAsync(job.RootPageId.ToString());
        Assert.Equal(4, Convert.ToInt32(dead.BasicProperties.Headers![PageTaskConsumer.AttemptHeader]));
        Assert.Equal(typeof(Crawler.Application.TransientCrawlException).FullName, HeaderString(dead, PageTaskConsumer.ErrorTypeHeader));
        Assert.Equal(job.JobId.ToString(), dead.BasicProperties.CorrelationId);
    }

    [Fact]
    public async Task Malformed_message_goes_straight_to_the_dlq()
    {
        using var host = await StartWorkerAsync(new FixtureSiteHandler());
        var messageId = $"poison-{Guid.NewGuid()}";

        await using (var connection = await new ConnectionFactory { Uri = new Uri(broker.Uri) }.CreateConnectionAsync())
        await using (var channel = await OpenChannelAsync(connection))
        {
            await channel.BasicPublishAsync(RabbitMqTopology.Exchange, RabbitMqTopology.RoutingKey, mandatory: true,
                new BasicProperties { MessageId = messageId, Persistent = true }, Encoding.UTF8.GetBytes("{ not json"));
        }

        var dead = await WaitForDeadLetterAsync(messageId);
        Assert.Equal(1, Convert.ToInt32(dead.BasicProperties.Headers![PageTaskConsumer.AttemptHeader]));
        Assert.Contains("Poison message", HeaderString(dead, PageTaskConsumer.ErrorHeader));
    }

    private async Task<IHost> StartWorkerAsync(FixtureSiteHandler site)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = db.ConnectionString,
            ["RabbitMq:Uri"] = broker.Uri,
            ["RabbitMq:OutboxPollInterval"] = "00:00:00.050",
            ["HttpFetch:MaxRetries"] = "0",
        });

        builder.Services.AddCrawlerCore(builder.Configuration).AddCrawlerWorker(builder.Configuration);
        builder.Services.Configure<RabbitMqOptions>(o => o.RetryDelays = FastRetryDelays);
        builder.Services.AddHttpClient<IPageFetcher, HttpPageFetcher>().ConfigurePrimaryHttpMessageHandler(() => site);

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static NewJob NewJob(string path) =>
        new(new Uri(FixtureSiteHandler.Root, path).AbsoluteUri, FixtureSiteHandler.Host, MaxDepth: 2, MaxPages: 200);

    private async Task<CrawlHarness.JobRow> WaitForJobAsync(Guid jobId)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();
        var deadline = DateTime.UtcNow + JobTimeout;

        while (true)
        {
            var job = await connection.QuerySingleAsync<CrawlHarness.JobRow>(
                "SELECT * FROM crawl_jobs WHERE id = @jobId", new { jobId });

            if (job.Status is "Completed" or "Failed" or "Canceled")
                return job;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Job {jobId} still {job.Status} after {JobTimeout}.");

            await Task.Delay(100);
        }
    }

    private async Task<BasicGetResult> WaitForDeadLetterAsync(string messageId)
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(broker.Uri) }.CreateConnectionAsync();
        await using var channel = await OpenChannelAsync(connection);
        var deadline = DateTime.UtcNow + JobTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var message = await channel.BasicGetAsync(RabbitMqTopology.DeadLetterQueue, autoAck: true);
            if (message is null)
                await Task.Delay(100);
            else if (message.BasicProperties.MessageId == messageId)
                return message;
        }

        throw new TimeoutException($"Message {messageId} never reached {RabbitMqTopology.DeadLetterQueue}.");
    }

    /// <summary>
    /// The worker declares the topology in the background after StartAsync returns, so the test
    /// declares it too (idempotent) rather than racing it.
    /// </summary>
    private static async Task<IChannel> OpenChannelAsync(IConnection connection)
    {
        var channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(channel, FastRetryDelays, CancellationToken.None);
        return channel;
    }

    private static string? HeaderString(BasicGetResult message, string header) =>
        message.BasicProperties.Headers?[header] is byte[] bytes ? Encoding.UTF8.GetString(bytes) : null;
}
