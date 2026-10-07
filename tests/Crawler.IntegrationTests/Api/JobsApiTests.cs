using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Crawler.IntegrationTests.Infrastructure;

namespace Crawler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class JobsApiTests(PostgresFixture db) : IDisposable
{
    private readonly ApiFactory _factory = new(db.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Create_returns_202_with_job_id_and_location()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/jobs", new { url = "HTTPS://Site.test/#intro", maxDepth = 1 });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var jobId = (await response.Content.ReadFromJsonAsync<JsonObject>())!["jobId"]!.GetValue<Guid>();
        Assert.EndsWith($"/api/jobs/{jobId}", response.Headers.Location!.ToString());

        var job = await client.GetFromJsonAsync<JsonObject>($"/api/jobs/{jobId}");
        Assert.Equal("Pending", (string?)job!["status"]);
        Assert.Equal("https://site.test/", (string?)job["url"]); // normalized
        Assert.Equal(1, (int?)job["maxDepth"]);
        Assert.Equal(200, (int?)job["maxPages"]);
        Assert.Equal(1, (int?)job["progress"]!["discovered"]);
        Assert.Null(job["startedAt"]);
    }

    [Fact]
    public async Task Max_depth_defaults_to_two()
    {
        var client = _factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/jobs", new { url = "https://site.test/" });
        var jobId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["jobId"]!.GetValue<Guid>();

        var job = await client.GetFromJsonAsync<JsonObject>($"/api/jobs/{jobId}");
        Assert.Equal(2, (int?)job!["maxDepth"]);
    }

    [Theory]
    [InlineData(null, null, "url")]
    [InlineData("", null, "url")]
    [InlineData("not a url", null, "url")]
    [InlineData("/relative/path", null, "url")]
    [InlineData("ftp://site.test/file", null, "url")]
    [InlineData("mailto:someone@site.test", null, "url")]
    [InlineData("https://site.test/", -1, "maxDepth")]
    [InlineData("https://site.test/", 6, "maxDepth")]
    public async Task Create_rejects_invalid_input(string? url, int? maxDepth, string invalidField)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/jobs", new { url, maxDepth });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(problem!["errors"]!.AsObject().ContainsKey(invalidField));
    }

    [Fact]
    public async Task Malformed_json_is_a_400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/jobs",
            new StringContent("{ \"url\": ", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_job_is_a_404_problem_with_correlation_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "test-correlation-123");

        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("test-correlation-123", response.Headers.GetValues("X-Correlation-Id").Single());
        var problem = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("Job not found", (string?)problem!["title"]);
        Assert.Equal("test-correlation-123", (string?)problem["correlationId"]);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/jobs/{Guid.NewGuid()}/tree")).StatusCode);
    }

    [Fact]
    public async Task Completed_job_returns_status_progress_and_tree()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/jobs", new { url = "https://site.test/" });
        var jobId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["jobId"]!.GetValue<Guid>();

        await new CrawlHarness(db.DataSource).RunToCompletionAsync(jobId);

        var job = await client.GetFromJsonAsync<JsonObject>($"/api/jobs/{jobId}");
        Assert.Equal("Completed", (string?)job!["status"]);
        Assert.NotNull(job["startedAt"]);
        Assert.NotNull(job["completedAt"]);
        Assert.Equal(10, (int?)job["progress"]!["discovered"]);
        Assert.Equal(0, (int?)job["progress"]!["pending"]);
        Assert.Equal(100.0, (double?)job["progress"]!["percent"]);

        var tree = await client.GetFromJsonAsync<JsonObject>($"/api/jobs/{jobId}/tree");
        var root = tree!["root"]!;
        Assert.Equal("https://site.test/", (string?)root["url"]);
        Assert.Equal(0.875, (double?)root["domainLinkRatio"]);
        Assert.Equal(6, root["children"]!.AsArray().Count); // the self-link is not a child

        var about = root["children"]!.AsArray().Single(c => (string?)c!["url"] == "https://site.test/about.html")!;
        var team = Assert.Single(about["children"]!.AsArray())!;
        Assert.Equal("https://site.test/team.html", (string?)team["url"]);
        Assert.Equal(2, (int?)team["depth"]);
        Assert.Equal(0.0, (double?)team["domainLinkRatio"]);

        var missing = root["children"]!.AsArray().Single(c => (string?)c!["url"] == "https://site.test/missing.html")!;
        Assert.Equal("Failed", (string?)missing["status"]);
        Assert.Equal(404, (int?)missing["httpStatus"]);
    }

    [Fact]
    public async Task History_is_paginated_most_recent_first()
    {
        var client = _factory.CreateClient();
        var ids = new List<Guid>();
        foreach (var path in new[] { "one", "two", "three" })
        {
            var created = await client.PostAsJsonAsync("/api/jobs", new { url = $"https://site.test/{path}" });
            ids.Add((await created.Content.ReadFromJsonAsync<JsonObject>())!["jobId"]!.GetValue<Guid>());
        }

        var page1 = await client.GetFromJsonAsync<JsonObject>("/api/jobs?page=1&pageSize=2");
        var page2 = await client.GetFromJsonAsync<JsonObject>("/api/jobs?page=2&pageSize=2");

        var firstPageIds = page1!["items"]!.AsArray().Select(i => i!["jobId"]!.GetValue<Guid>()).ToList();
        Assert.Equal([ids[2], ids[1]], firstPageIds);
        Assert.Equal(ids[0], page2!["items"]!.AsArray()[0]!["jobId"]!.GetValue<Guid>());
        Assert.True((long?)page1["totalCount"] >= 3);
        Assert.Equal(2, (int?)page1["pageSize"]);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task History_rejects_invalid_paging(string query)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/jobs?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_stops_an_active_job_once()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/jobs", new { url = "https://site.test/" });
        var jobId = (await created.Content.ReadFromJsonAsync<JsonObject>())!["jobId"]!.GetValue<Guid>();

        var canceled = await client.PostAsync($"/api/jobs/{jobId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        var job = await canceled.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("Canceled", (string?)job!["status"]);
        Assert.NotNull(job["completedAt"]);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/jobs/{jobId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Liveness_endpoint_is_healthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
