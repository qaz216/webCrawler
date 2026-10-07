using Crawler.Application.Abstractions;
using Crawler.Domain.Jobs;

namespace Crawler.Application.Jobs;

public sealed record PageTreeNode(
    Guid PageId,
    string Url,
    int Depth,
    PageStatus Status,
    int? HttpStatus,
    string? Error,
    double? DomainLinkRatio,
    int? OutgoingLinkCount,
    Guid? DuplicateOfPageId,
    string? DuplicateOfUrl,
    DateTime? FinishedAt,
    IReadOnlyList<PageTreeNode> Children);

/// <summary>
/// Builds the crawl tree from flat page rows. The site is a graph; the tree is the spanning tree
/// formed by each page's first discoverer (parent_page_id). O(n), children sorted by URL.
/// </summary>
public static class PageTreeBuilder
{
    public static PageTreeNode? Build(IReadOnlyList<PageRecord> pages)
    {
        var root = pages.FirstOrDefault(p => p.ParentPageId is null);
        if (root is null)
            return null;

        var childrenByParent = pages
            .Where(p => p.ParentPageId is not null)
            .ToLookup(p => p.ParentPageId!.Value);

        var visited = new HashSet<Guid>();
        return BuildNode(root, childrenByParent, visited);
    }

    private static PageTreeNode BuildNode(PageRecord page, ILookup<Guid, PageRecord> childrenByParent, HashSet<Guid> visited)
    {
        visited.Add(page.PageId);

        var children = childrenByParent[page.PageId]
            .Where(child => !visited.Contains(child.PageId)) // defensive: parent links can't form cycles
            .OrderBy(child => child.Url, StringComparer.Ordinal)
            .Select(child => BuildNode(child, childrenByParent, visited))
            .ToList();

        return new PageTreeNode(page.PageId, page.Url, page.Depth, page.Status, page.HttpStatus, page.Error,
            page.DomainLinkRatio, page.OutgoingLinkCount, page.DuplicateOfPageId, page.DuplicateOfUrl,
            page.FinishedAt, children);
    }
}
