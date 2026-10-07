using System.Net;
using Crawler.Application;
using Crawler.Application.Abstractions;
using Crawler.Application.Jobs;
using Crawler.Application.Pages;
using Crawler.Infrastructure.Health;
using Crawler.Infrastructure.Http;
using Crawler.Infrastructure.Messaging;
using Crawler.Infrastructure.Outbox;
using Crawler.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;

namespace Crawler.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Shared by the API and the worker: PostgreSQL, the crawl store, RabbitMQ publishing,
    /// the outbox dispatcher and readiness health checks (tag "ready").
    /// </summary>
    public static IServiceCollection AddCrawlerCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CrawlerOptions>(configuration.GetSection(CrawlerOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddSingleton(sp => new NpgsqlDataSourceBuilder(connectionString)
            .UseLoggerFactory(sp.GetRequiredService<ILoggerFactory>())
            .Build());
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<ICrawlStore, CrawlStore>();
        services.AddSingleton<OutboxStore>();
        services.AddScoped<JobService>();

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<MessagePublisher>();
        services.AddHostedService<OutboxDispatcher>();

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

        return services;
    }

    /// <summary>API only: read queries for job status, tree and history.</summary>
    public static IServiceCollection AddCrawlerApi(this IServiceCollection services)
    {
        services.AddSingleton<ICrawlQueries, CrawlQueries>();
        return services;
    }

    /// <summary>Worker only: the page fetcher (with timeouts, retries, redirects), the handler and the consumer.</summary>
    public static IServiceCollection AddCrawlerWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HttpFetchOptions>(configuration.GetSection(HttpFetchOptions.SectionName));

        services.AddHttpClient<IPageFetcher, HttpPageFetcher>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<HttpFetchOptions>>().Value;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = sp.GetRequiredService<IOptions<HttpFetchOptions>>().Value.MaxRedirects,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .AddResilienceHandler("page-fetch", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<HttpFetchOptions>>().Value;

                // Outer → inner: total budget, retries (5xx/408/429/network/timeouts, honours Retry-After), per-attempt timeout.
                pipeline.AddTimeout(options.TotalTimeout);
                if (options.MaxRetries > 0)
                {
                    pipeline.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetries,
                        Delay = options.RetryBaseDelay,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                    });
                }
                pipeline.AddTimeout(options.AttemptTimeout);
            });

        services.AddScoped<PageTaskHandler>();
        services.AddHostedService<PageTaskConsumer>();

        return services;
    }
}
