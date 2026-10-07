using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace Crawler.IntegrationTests.Infrastructure;

/// <summary>
/// Serves Fixtures/site as https://site.test/ (and its subdomains) without a network. "/dir/" maps to dir/index.html,
/// .pdf files are served as application/pdf, missing files are 404, /flaky.html returns
/// 503 on its first request and /down.html always returns 503. Every request is counted so tests can assert "fetched exactly once".
/// </summary>
public sealed class FixtureSiteHandler : HttpMessageHandler
{
    public const string Host = "site.test";
    public static readonly Uri Root = new($"https://{Host}/");

    /// <summary>A host that answers every request with a 301 to the same path on <see cref="Host"/> (like google.com → www.google.com).</summary>
    public const string RedirectingHost = "alias.test";

    private static readonly string SiteRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "site");

    private readonly ConcurrentDictionary<string, int> _requests = new();

    /// <summary>Requests for <paramref name="path"/> on <paramref name="host"/> (default: site.test).</summary>
    public int RequestCount(string path, string host = Host) => _requests.GetValueOrDefault(host + path);

    /// <summary>Request counts keyed by host + path, e.g. "site.test/about.html".</summary>
    public IReadOnlyDictionary<string, int> Requests => _requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var count = _requests.AddOrUpdate(uri.Host + uri.AbsolutePath, 1, (_, n) => n + 1);

        var response = string.Equals(uri.Host, RedirectingHost, StringComparison.OrdinalIgnoreCase)
            ? new HttpResponseMessage(HttpStatusCode.MovedPermanently)
            {
                Headers = { Location = new UriBuilder(uri) { Host = Host }.Uri },
            }
            : Respond(uri, count);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    private static HttpResponseMessage Respond(Uri uri, int requestNumber)
    {
        // Subdomains (www.site.test, blog.site.test) serve the same files, like many real sites.
        var host = uri.Host.ToLowerInvariant();
        if (host != Host && !host.EndsWith("." + Host, StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        if (uri.AbsolutePath == "/flaky.html" && requestNumber == 1)
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        if (uri.AbsolutePath == "/down.html")
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var relative = uri.AbsolutePath.TrimStart('/');
        if (relative.Length == 0 || relative.EndsWith('/'))
            relative += "index.html";

        var file = Path.Combine(SiteRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(file))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        var content = new ByteArrayContent(File.ReadAllBytes(file));
        content.Headers.ContentType = file.EndsWith(".pdf", StringComparison.Ordinal)
            ? new MediaTypeHeaderValue("application/pdf")
            : new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
