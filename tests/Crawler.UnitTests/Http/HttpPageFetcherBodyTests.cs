using System.Net;
using System.Text;
using Crawler.Application.Abstractions;
using Crawler.Domain.Urls;
using Crawler.Infrastructure.Http;

namespace Crawler.UnitTests.Http;

/// <summary>How much of a response body the fetcher reads (regression for www.cnn.com: links after ~2 MB).</summary>
public class HttpPageFetcherBodyTests
{
    private sealed class FixedPage(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
                RequestMessage = request,
            });
    }

    private static readonly Uri Url = new("https://site.test/");

    private static Task<FetchResult> Fetch(string html) =>
        new HttpPageFetcher(new HttpClient(new FixedPage(html))).FetchAsync(Url, CancellationToken.None);

    /// <summary>A large inline script in &lt;head&gt;, like news homepages have.</summary>
    private static string Head(int megabytes) =>
        "<html><head><script>" + new string('x', megabytes * 1024 * 1024) + "</script></head>";

    [Fact]
    public async Task Reads_links_that_start_after_several_megabytes_of_head()
    {
        var html = Head(5) + "<body><a href='/news'>News</a><a href='https://other.test/'>Other</a></body></html>";

        var result = await Fetch(html);

        Assert.Equal(FetchKind.Html, result.Kind);
        Assert.Equal(html.Length, result.Html!.Length);
        Assert.Equal(["https://site.test/news", "https://other.test/"], LinkExtractor.Extract(result.Html, Url));
    }

    [Fact]
    public async Task Stops_reading_at_the_cap()
    {
        var html = Head(11) + "<body><a href='/beyond-the-cap'>x</a></body></html>";

        var result = await Fetch(html);

        Assert.Equal(HttpPageFetcher.MaxBodyBytes, Encoding.UTF8.GetByteCount(result.Html!));
        Assert.Empty(LinkExtractor.Extract(result.Html!, Url));
    }

    [Fact]
    public async Task Small_pages_are_read_whole()
    {
        const string html = "<html><body><a href='/a'>A</a></body></html>";

        var result = await Fetch(html);

        Assert.Equal(html, result.Html);
    }
}
