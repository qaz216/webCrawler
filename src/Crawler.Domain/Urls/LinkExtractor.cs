using AngleSharp.Html.Parser;

namespace Crawler.Domain.Urls;

/// <summary>
/// Extracts the distinct, normalized http(s) links from an HTML document,
/// in document order. Honours a &lt;base href&gt; element when present.
/// </summary>
public static class LinkExtractor
{
    private static readonly HtmlParser Parser = new();

    public static IReadOnlyList<string> Extract(string html, Uri pageUri)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageUri);

        using var document = Parser.ParseDocument(html);

        var baseUri = ResolveBaseUri(document.QuerySelector("base[href]")?.GetAttribute("href"), pageUri);

        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var anchor in document.QuerySelectorAll("a[href], area[href]"))
        {
            var normalized = UrlNormalizer.Normalize(anchor.GetAttribute("href"), baseUri);
            if (normalized is not null && seen.Add(normalized))
                links.Add(normalized);
        }

        return links;
    }

    private static Uri ResolveBaseUri(string? baseHref, Uri pageUri) =>
        !string.IsNullOrWhiteSpace(baseHref) && Uri.TryCreate(pageUri, baseHref.Trim(), out var resolved)
            ? resolved
            : pageUri;
}
