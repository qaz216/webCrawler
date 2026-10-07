namespace Crawler.Application;

public sealed class CrawlerOptions
{
    public const string SectionName = "Crawler";

    public int DefaultMaxDepth { get; set; } = 2;

    public int MaxAllowedDepth { get; set; } = 5;

    /// <summary>Safety cap on pages per job (README → Crawling rules).</summary>
    public int MaxPagesPerJob { get; set; } = 200;

    /// <summary>How long a worker owns a page before another delivery may take it over.</summary>
    public TimeSpan PageLease { get; set; } = TimeSpan.FromMinutes(2);
}
