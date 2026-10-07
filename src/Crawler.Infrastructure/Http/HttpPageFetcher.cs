using System.Net.Http.Headers;
using System.Text;
using Crawler.Application;
using Crawler.Application.Abstractions;

namespace Crawler.Infrastructure.Http;

/// <summary>
/// Fetches pages over HTTP. Classifies outcomes for the retry policy (README → Retry policy):
/// 408, 429, 5xx, timeouts and connection errors are transient (thrown); other 4xx and
/// non-HTML responses are permanent results (returned). Bodies are capped at <see cref="MaxBodyBytes"/>.
/// Per-attempt timeouts, in-process retries and redirects are configured on the injected HttpClient.
/// </summary>
public sealed class HttpPageFetcher(HttpClient httpClient) : IPageFetcher
{
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    private static readonly string[] HtmlMediaTypes = ["text/html", "application/xhtml+xml"];

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");

        using var response = await SendAsync(request, cancellationToken);

        var statusCode = (int)response.StatusCode;
        var finalUri = response.RequestMessage?.RequestUri ?? url;

        if (IsTransientStatus(statusCode))
            throw new TransientCrawlException($"HTTP {statusCode} from {url}");

        if (!response.IsSuccessStatusCode)
            return FetchResult.HttpError(finalUri, statusCode);

        var contentType = response.Content.Headers.ContentType;
        if (!IsHtml(contentType))
            return FetchResult.NotHtml(finalUri, statusCode, contentType?.MediaType);

        var html = await ReadBodyAsync(response.Content, contentType, cancellationToken);
        return FetchResult.FromHtml(finalUri, statusCode, contentType?.MediaType, html);
    }

    public static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 || statusCode >= 500;

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TransientCrawlException($"Request to {request.RequestUri} failed: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransientCrawlException($"Request to {request.RequestUri} timed out", ex);
        }
    }

    private static bool IsHtml(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType is { } mediaType &&
        HtmlMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);

    private static async Task<string> ReadBodyAsync(
        HttpContent content, MediaTypeHeaderValue? contentType, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);

        var buffer = new byte[MaxBodyBytes];
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        // Anything past the cap is ignored: links in the first 2 MB are still extracted.
        return GetEncoding(contentType?.CharSet).GetString(buffer, 0, total);
    }

    private static Encoding GetEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
