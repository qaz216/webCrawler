using System.Net;

namespace Crawler.Domain.Urls;

/// <summary>
/// The root (registrable) domain of a host: what a person would call "the site".
/// <c>www.newsmax.com</c>, <c>w3.newsmax.com</c> and <c>newsmax.com</c> all have the root domain
/// <c>newsmax.com</c>; <c>news.bbc.co.uk</c> has <c>bbc.co.uk</c> (not <c>co.uk</c>).
/// <para>
/// Uses the last two labels, or the last three when the host ends in a common two-part country
/// suffix (co.uk, com.au, co.il, …). The full Public Suffix List would also cover rarer suffixes and
/// shared-hosting domains (user1.github.io vs user2.github.io would count as one site).
/// </para>
/// </summary>
public static class RootDomain
{
    /// <summary>Second-level labels that, under a two-letter country TLD, are part of the suffix.</summary>
    private static readonly HashSet<string> CountrySecondLevels = new(StringComparer.Ordinal)
    {
        "ac", "co", "com", "edu", "gob", "gov", "govt", "go", "info", "ltd", "me", "mil", "ne", "net",
        "nhs", "nic", "or", "org", "plc", "sch", "web",
    };

    public static string Of(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(normalized.Trim('[', ']'), out _) || !normalized.Contains('.'))
            return normalized; // IP addresses and single-label hosts (localhost) are their own root

        var labels = normalized.Split('.');
        var take = labels.Length >= 3 && labels[^1].Length == 2 && CountrySecondLevels.Contains(labels[^2]) ? 3 : 2;
        return string.Join('.', labels[^take..]);
    }

    /// <summary>True when both hosts belong to the same root domain (same site).</summary>
    public static bool SameSite(string hostA, string hostB) =>
        string.Equals(Of(hostA), Of(hostB), StringComparison.Ordinal);
}
