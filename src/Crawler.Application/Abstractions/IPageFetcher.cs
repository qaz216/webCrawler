namespace Crawler.Application.Abstractions;

public interface IPageFetcher
{
    /// <summary>
    /// Fetches a page. Permanent outcomes (HTML, non-HTML, 4xx) are returned as a <see cref="FetchResult"/>;
    /// transient failures throw <see cref="TransientCrawlException"/>.
    /// </summary>
    Task<FetchResult> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public enum FetchKind
{
    Html,
    NotHtml,
    HttpError,

    /// <summary>Not fetched by policy (e.g. the host resolves to a private network address).</summary>
    Refused,
}

public sealed record FetchResult(FetchKind Kind, Uri FinalUri, int StatusCode, string? ContentType, string? Html, string? Error = null)
{
    public static FetchResult Refused(Uri url, string reason) =>
        new(FetchKind.Refused, url, 0, null, null, reason);

    public static FetchResult FromHtml(Uri finalUri, int statusCode, string? contentType, string html) =>
        new(FetchKind.Html, finalUri, statusCode, contentType, html);

    public static FetchResult NotHtml(Uri finalUri, int statusCode, string? contentType) =>
        new(FetchKind.NotHtml, finalUri, statusCode, contentType, null);

    public static FetchResult HttpError(Uri finalUri, int statusCode) =>
        new(FetchKind.HttpError, finalUri, statusCode, null, null);
}
