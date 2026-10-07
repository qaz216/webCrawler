using Crawler.Application.Abstractions;
using Crawler.Application.Jobs;
using Crawler.Domain.Jobs;

namespace Crawler.UnitTests.Jobs;

public class PageTreeBuilderTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid About = Guid.NewGuid();
    private static readonly Guid Blog = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();

    private static PageRecord Page(Guid id, Guid? parent, string path, int depth, double? ratio = 0.5) =>
        new(id, parent, $"https://example.com{path}", depth, PageStatus.Completed, 200, null, ratio, 4);

    [Fact]
    public void Builds_hierarchy_from_parent_links()
    {
        var tree = PageTreeBuilder.Build(
        [
            Page(Team, About, "/team", 2),
            Page(Blog, Root, "/blog", 1),
            Page(Root, null, "/", 0),
            Page(About, Root, "/about", 1),
        ]);

        Assert.NotNull(tree);
        Assert.Equal(Root, tree.PageId);
        Assert.Equal(["https://example.com/about", "https://example.com/blog"], tree.Children.Select(c => c.Url)); // sorted by URL
        Assert.Equal(Team, Assert.Single(tree.Children[0].Children).PageId);
        Assert.Empty(tree.Children[1].Children);
    }

    [Fact]
    public void Carries_page_metrics_onto_nodes()
    {
        var tree = PageTreeBuilder.Build([Page(Root, null, "/", 0, ratio: 0.875)])!;

        Assert.Equal(0.875, tree.DomainLinkRatio);
        Assert.Equal(4, tree.OutgoingLinkCount);
        Assert.Equal(PageStatus.Completed, tree.Status);
    }

    [Fact]
    public void No_pages_means_no_tree() =>
        Assert.Null(PageTreeBuilder.Build([]));

    [Fact]
    public void Orphans_are_ignored_rather_than_crashing()
    {
        var tree = PageTreeBuilder.Build([Page(Root, null, "/", 0), Page(Team, Guid.NewGuid(), "/orphan", 2)])!;

        Assert.Empty(tree.Children);
    }
}
