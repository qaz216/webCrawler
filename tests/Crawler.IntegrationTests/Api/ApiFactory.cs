using Crawler.Infrastructure.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Crawler.IntegrationTests.Api;

/// <summary>
/// The real API (Program.cs) against the test PostgreSQL. The outbox dispatcher is removed so tests
/// can drain the outbox through <see cref="Infrastructure.CrawlHarness"/> instead of a broker.
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1/"); // unused: no dispatcher
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");

        builder.ConfigureTestServices(services =>
        {
            var dispatchers = services
                .Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(OutboxDispatcher))
                .ToList();
            foreach (var dispatcher in dispatchers)
                services.Remove(dispatcher);
        });
    }
}
