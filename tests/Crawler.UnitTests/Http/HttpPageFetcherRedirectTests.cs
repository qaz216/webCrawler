using System.Net;
using System.Text;
using Crawler.Application.Abstractions;
using Crawler.Infrastructure.Http;

namespace Crawler.UnitTests.Http;

/// <summary>The fetcher follows redirects itself, including https → http (which HttpClient refuses).</summary>
public class HttpPageFetcherRedirectTests
{
    /// <summary>Serves a fixed map of URL → response; records every request.</summary>
    private sealed class StubSite(Dictionary<string, Func<HttpResponseMessage>> routes) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            var response = routes.TryGetValue(url, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.Found) =>
        new(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static HttpResponseMessage Html(string body = "<a href='/x'>x</a>") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

    private static HttpPageFetcher Fetcher(StubSite site) => new(new HttpClient(site));

    [Fact]
    public async Task Follows_https_to_http_downgrade()
    {
        // The real case: https://www.scrapethissite.com/lessons/ → 302 http://www.scrapethissite.com/lessons/sign-up/
        var site = new StubSite(new()
        {
            ["https://site.test/lessons/"] = () => Redirect("http://site.test/lessons/sign-up/"),
            ["http://site.test/lessons/sign-up/"] = () => Html(),
        });

        var result = await Fetcher(site).FetchAsync(new Uri("https://site.test/lessons/"), CancellationToken.None);

        Assert.Equal(FetchKind.Html, result.Kind);
        Assert.Equal("http://site.test/lessons/sign-up/", result.FinalUri.AbsoluteUri);
        Assert.Equal(200, result.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Follows_every_redirect_status(HttpStatusCode status)
    {
        var site = new StubSite(new()
        {
            ["https://site.test/old"] = () => Redirect("https://site.test/new", status),
            ["https://site.test/new"] = () => Html(),
        });

        var result = await Fetcher(site).FetchAsync(new Uri("https://site.test/old"), CancellationToken.None);

        Assert.Equal(FetchKind.Html, result.Kind);
        Assert.Equal("https://site.test/new", result.FinalUri.AbsoluteUri);
    }

    [Fact]
    public async Task Resolves_relative_locations_against_the_current_url()
    {
        var site = new StubSite(new()
        {
            ["https://site.test/a/b"] = () => Redirect("../c"),
            ["https://site.test/c"] = () => Redirect("/d?x=1"),
            ["https://site.test/d?x=1"] = () => Html(),
        });

        var result = await Fetcher(site).FetchAsync(new Uri("https://site.test/a/b"), CancellationToken.None);

        Assert.Equal("https://site.test/d?x=1", result.FinalUri.AbsoluteUri);
        Assert.Equal(["https://site.test/a/b", "https://site.test/c", "https://site.test/d?x=1"], site.Requested);
    }

    [Fact]
    public async Task Stops_redirect_loops()
    {
        var site = new StubSite(new()
        {
            ["https://site.test/ping"] = () => Redirect("https://site.test/pong"),
            ["https://site.test/pong"] = () => Redirect("https://site.test/ping"),
        });

        var result = await Fetcher(site).FetchAsync(new Uri("https://site.test/ping"), CancellationToken.None);

        Assert.Equal(FetchKind.Refused, result.Kind);
        Assert.Contains("Too many redirects", result.Error);
        Assert.Equal(6, site.Requested.Count); // the original request + 5 redirects
    }

    [Fact]
    public async Task Refuses_redirects_to_non_web_addresses()
    {
        var site = new StubSite(new() { ["https://site.test/mail"] = () => Redirect("mailto:someone@site.test") });

        var result = await Fetcher(site).FetchAsync(new Uri("https://site.test/mail"), CancellationToken.None);

        Assert.Equal(FetchKind.Refused, result.Kind);
        Assert.Contains("non-web address", result.Error);
    }

    [Fact]
    public async Task Redirect_without_location_is_a_plain_http_error()
    {
        var site = new StubSite(new() { ["https://site.test/odd"] = () => new HttpResponseMessage(HttpStatusCode.Found) });

        var result = await Fetcher(site).FetchAsync(new Uri("https://site.test/odd"), CancellationToken.None);

        Assert.Equal(FetchKind.HttpError, result.Kind);
        Assert.Equal(302, result.StatusCode);
    }
}
