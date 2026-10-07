using Crawler.Infrastructure;
using Crawler.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;

// Crawl Worker (Service B): consumes page tasks from RabbitMQ, fetches and parses pages,
// stores results, and publishes child tasks via the outbox. Hosts only health endpoints over HTTP.
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "crawl-worker")
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services
    .AddCrawlerCore(builder.Configuration)
    .AddCrawlerWorker(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.Services.GetRequiredService<DatabaseMigrator>().MigrateWithRetryAsync(app.Lifetime.ApplicationStopping);

app.Run();
