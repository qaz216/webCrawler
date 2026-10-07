namespace Crawler.Domain.Urls;

/// <summary>
/// Turns raw hrefs into a canonical absolute URL string used for de-duplication.
/// Rules (see README → Crawling rules):
/// resolve relative against the base, http/https only, drop fragment and user info,
/// lowercase scheme and host, drop default port, empty path becomes "/".
/// Path case, trailing slash and query string are preserved as-is.
/// </summary>
public static class UrlNormalizer
{
    public const int MaxUrlLength = 2048;

    private const UriComponents CanonicalComponents =
        UriComponents.SchemeAndServer | UriComponents.PathAndQuery;

    /// <summary>Normalizes an absolute URL, e.g. the URL a job was submitted with.</summary>
    public static string? Normalize(string? absoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl))
            return null;

        return Uri.TryCreate(absoluteUrl.Trim(), UriKind.Absolute, out var uri)
            ? Canonicalize(uri)
            : null;
    }

    /// <summary>
    /// Resolves an href found on a page against <paramref name="baseUri"/> and normalizes it.
    /// Returns null for links that are not crawlable (mailto:, tel:, javascript:, malformed, too long).
    /// </summary>
    public static string? Normalize(string? href, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        if (href is null)
            return null;

        // HTML strips leading/trailing whitespace from URLs; an empty href refers to the page itself.
        var trimmed = href.Trim();

        return Uri.TryCreate(baseUri, trimmed, out var resolved)
            ? Canonicalize(resolved)
            : null;
    }

    private static string? Canonicalize(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !IsHttp(uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        // SchemeAndServer omits user info and default ports; Uri already lowercases
        // scheme and host, removes dot segments and gives an empty path as "/".
        var canonical = uri.GetComponents(CanonicalComponents, UriFormat.UriEscaped);

        return canonical.Length <= MaxUrlLength ? canonical : null;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
