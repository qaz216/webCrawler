using System.Net.Http.Headers;
using System.Text;
using Crawler.Application;
using Crawler.Application.Abstractions;
using Microsoft.Extensions.Options;
using Polly;

namespace Crawler.Infrastructure.Http;

/// <summary>
/// Fetches pages over HTTP. Classifies outcomes for the retry policy (README → Retry policy):
/// 408, 429, 5xx, timeouts and connection errors are transient (thrown); other 4xx and
/// non-HTML responses are permanent results (returned). Bodies are capped at <see cref="MaxBodyBytes"/>.
/// <para>
/// Redirects are followed here rather than by HttpClient, which refuses https → http downgrades
/// (common on real sites, e.g. a section that redirects to an http sign-up page). Every hop is a new
/// connection, so the SSRF guard checks each one. Per-attempt timeouts and in-process retries are
/// configured on the injected HttpClient.
/// </para>
/// </summary>
public sealed class HttpPageFetcher(HttpClient httpClient, IOptions<HttpFetchOptions>? options = null) : IPageFetcher
{
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    private static readonly string[] HtmlMediaTypes = ["text/html", "application/xhtml+xml"];

    private readonly int _maxRedirects = options?.Value.MaxRedirects ?? new HttpFetchOptions().MaxRedirects;

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var currentUri = url;
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");

            HttpResponseMessage sent;
            try
            {
                sent = await SendAsync(request, cancellationToken);
            }
            catch (BlockedDestinationException blocked)
            {
                return FetchResult.Refused(currentUri, blocked.Message);
            }

            using var response = sent;

            if (!IsRedirect((int)response.StatusCode) || response.Headers.Location is not { } location)
                return await ReadResponseAsync(response, currentUri, cancellationToken);

            if (redirects >= _maxRedirects)
                return FetchResult.Refused(currentUri, $"Too many redirects (more than {_maxRedirects}).");

            var next = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
                return FetchResult.Refused(currentUri, $"Redirects to a non-web address ({next.Scheme}:).");

            currentUri = next;
        }
    }

    private static bool IsRedirect(int statusCode) => statusCode is 301 or 302 or 303 or 307 or 308;

    private static async Task<FetchResult> ReadResponseAsync(HttpResponseMessage response, Uri finalUri, CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        if (IsTransientStatus(statusCode))
            throw new TransientCrawlException($"HTTP {statusCode} from {finalUri}");

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

    internal static BlockedDestinationException? FindBlocked(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is BlockedDestinationException blocked)
                return blocked;
        }
        return null;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex) when (FindBlocked(ex) is { } blocked)
        {
            throw blocked; // permanent: FetchAsync turns it into a Refused result, no retries
        }
        catch (HttpRequestException ex)
        {
            throw new TransientCrawlException($"Request to {request.RequestUri} failed: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransientCrawlException($"Request to {request.RequestUri} timed out", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            // Resilience pipeline gave up (attempt/total timeout).
            throw new TransientCrawlException($"Request to {request.RequestUri} was rejected: {ex.Message}", ex);
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
