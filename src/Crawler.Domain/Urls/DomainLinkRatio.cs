namespace Crawler.Domain.Urls;

/// <summary>
/// Domain Link Ratio = (# outgoing links within the starting domain) / (total # outgoing links).
/// Links are expected to be normalized (see <see cref="UrlNormalizer"/>); duplicates count once.
/// A page with no outgoing links has a ratio of 0.
/// </summary>
public static class DomainLinkRatio
{
    public static double Calculate(IEnumerable<string> normalizedLinks, string startingHost)
    {
        ArgumentNullException.ThrowIfNull(normalizedLinks);
        ArgumentException.ThrowIfNullOrWhiteSpace(startingHost);

        var distinct = normalizedLinks.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0)
            return 0;

        var internalCount = distinct.Count(link => IsInternal(link, startingHost));
        return (double)internalCount / distinct.Count;
    }

    /// <summary>
    /// True when the link stays within the starting domain, measured by root domain: www., w3. and
    /// any other subdomain of the same site are internal (see <see cref="RootDomain"/>).
    /// </summary>
    public static bool IsInternal(string normalizedLink, string startingHost) =>
        Uri.TryCreate(normalizedLink, UriKind.Absolute, out var uri)
        && RootDomain.SameSite(uri.Host, startingHost);
}
