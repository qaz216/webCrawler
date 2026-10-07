using System.Security.Cryptography;
using System.Text;

namespace Crawler.Domain.Content;

/// <summary>
/// Identifies pages with identical content so URL aliases (/ vs /index.html, tracking parameters,
/// trailing-slash variants) are crawled once. SHA-256 of the HTML body as lowercase hex.
/// Line endings and surrounding whitespace are ignored; anything else that differs between loads
/// (timestamps, CSRF tokens) makes pages distinct, which errs on the side of crawling.
/// </summary>
public static class ContentFingerprint
{
    public static string Compute(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var canonical = html.ReplaceLineEndings("\n").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
