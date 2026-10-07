namespace Crawler.Infrastructure.Http;

public sealed class HttpFetchOptions
{
    public const string SectionName = "HttpFetch";

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>In-process retries for quick blips, before the message-level retry tiers kick in.</summary>
    public int MaxRetries { get; set; } = 2;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public int MaxRedirects { get; set; } = 5;

    /// <summary>
    /// SSRF protection: refuse hosts that resolve to private, loopback or link-local addresses
    /// (incl. cloud metadata). On by default; turn off only to crawl a site on your own network.
    /// </summary>
    public bool BlockPrivateNetworks { get; set; } = true;

    public string UserAgent { get; set; } = "WebCrawler/1.0 (+https://github.com/qaz216/webCrawler)";
}
