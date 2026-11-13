namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;

/// <summary>
/// Tests for <see cref="ProjectTreeBuilder"/> — filtering and nesting the
/// project sidebar's file tree from relative paths.
/// </summary>
public class ProjectTreeBuilderTests
{
    // --- FilterDocumentPaths ---

    [Fact]
    public void Filter_KeepsAdocAndAsciidoc_CaseInsensitive()
    {
        var kept = ProjectTreeBuilder.FilterDocumentPaths(
            ["a.adoc", "b.ASCIIDOC", "c.txt", "d.md", "e.adoc.bak"]).ToList();
        Assert.Equal(["a.adoc", "b.ASCIIDOC"], kept);
    }

    [Fact]
    public void Filter_DropsDotDirectories()
    {
        var kept = ProjectTreeBuilder.FilterDocumentPaths(
            [".git/x.adoc", "docs/.cache/y.adoc", "docs/ok.adoc"]).ToList();
        Assert.Equal(["docs/ok.adoc"], kept);
    }

    // --- Build ---

    [Fact]
    public void Build_NestsByDirectory()
    {
        var roots = ProjectTreeBuilder.Build(@"C:\proj", ["docs/guide.adoc", "readme.adoc"]);

        Assert.Equal(2, roots.Count);
        var docs = roots[0];
        Assert.True(docs.IsDirectory);
        Assert.Equal("docs", docs.Name);
        var guide = Assert.Single(docs.Children);
        Assert.False(guide.IsDirectory);
        Assert.Equal("guide.adoc", guide.Name);
        Assert.Equal(Path.Combine(@"C:\proj", "docs", "guide.adoc"), guide.FullPath);

        Assert.Equal("readme.adoc", roots[1].Name);
    }

    [Fact]
    public void Build_DirectoriesFirst_ThenAlphabetical()
    {
        var roots = ProjectTreeBuilder.Build(@"C:\p",
            ["zzz.adoc", "alpha/x.adoc", "beta/y.adoc", "aaa.adoc"]);

        Assert.Equal(["alpha", "beta", "aaa.adoc", "zzz.adoc"], roots.Select(r => r.Name));
        Assert.Equal([true, true, false, false], roots.Select(r => r.IsDirectory));
    }

    [Fact]
    public void Build_MergesSharedDirectories()
    {
        var roots = ProjectTreeBuilder.Build(@"C:\p", ["docs/a.adoc", "docs/b.adoc"]);
        var docs = Assert.Single(roots);
        Assert.Equal(2, docs.Children.Count);
    }

    [Fact]
    public void Build_Empty_IsEmpty()
    {
        Assert.Empty(ProjectTreeBuilder.Build(@"C:\p", []));
    }

    [Fact]
    public void Build_HandlesBackslashSeparators()
    {
        var roots = ProjectTreeBuilder.Build(@"C:\p", [@"docs\sub\deep.adoc"]);
        var docs = Assert.Single(roots);
        var sub = Assert.Single(docs.Children);
        var deep = Assert.Single(sub.Children);
        Assert.Equal("deep.adoc", deep.Name);
        Assert.True(docs.IsDirectory && sub.IsDirectory && !deep.IsDirectory);
    }
}
