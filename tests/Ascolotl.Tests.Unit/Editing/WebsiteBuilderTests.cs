namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="WebsiteBuilder"/> — the pure parts of website export:
/// path mapping, link rewriting, title extraction, and the index page.
/// </summary>
public class WebsiteBuilderTests
{
    // --- Path mapping ---

    [Theory]
    [InlineData("guide.adoc", "guide.html")]
    [InlineData("docs/setup.asciidoc", "docs/setup.html")]
    [InlineData(@"docs\sub\deep.adoc", "docs/sub/deep.html")]
    public void ToHtmlPath_MapsExtensionAndSeparators(string input, string expected)
    {
        Assert.Equal(expected, WebsiteBuilder.ToHtmlPath(input));
    }

    // --- Link rewriting ---

    [Fact]
    public void RewriteDocLinks_RewritesAdocHrefs()
    {
        var html = """<a href="other.adoc">Other</a>""";
        Assert.Equal("""<a href="other.html">Other</a>""", WebsiteBuilder.RewriteDocLinks(html));
    }

    [Fact]
    public void RewriteDocLinks_PreservesAnchors()
    {
        var html = """<a href="guide.adoc#install">Install</a>""";
        Assert.Equal("""<a href="guide.html#install">Install</a>""", WebsiteBuilder.RewriteDocLinks(html));
    }

    [Fact]
    public void RewriteDocLinks_LeavesExternalUrlsAlone()
    {
        var html = """<a href="https://example.com/x.adoc">ext</a>""";
        Assert.Equal(html, WebsiteBuilder.RewriteDocLinks(html));
    }

    [Fact]
    public void RewriteDocLinks_HandlesRelativeFolders()
    {
        var html = """<a href="../intro/start.asciidoc">Start</a>""";
        Assert.Equal("""<a href="../intro/start.html">Start</a>""", WebsiteBuilder.RewriteDocLinks(html));
    }

    // --- Title extraction ---

    [Fact]
    public void ExtractTitle_UsesDocTitle()
    {
        Assert.Equal("User Guide", WebsiteBuilder.ExtractTitle("= User Guide\n\nText.", "guide.adoc"));
    }

    [Fact]
    public void ExtractTitle_SkipsCommentsAndAttributes()
    {
        var src = "// header comment\n:toc:\n= Real Title\n";
        Assert.Equal("Real Title", WebsiteBuilder.ExtractTitle(src, "x.adoc"));
    }

    [Fact]
    public void ExtractTitle_FallsBackToFileName()
    {
        Assert.Equal("notes", WebsiteBuilder.ExtractTitle("Just prose, no title.", "notes.adoc"));
    }

    // --- Index ---

    [Fact]
    public void HasOwnIndex_DetectsRootIndexOnly()
    {
        Assert.True(WebsiteBuilder.HasOwnIndex(["index.adoc", "other.adoc"]));
        Assert.False(WebsiteBuilder.HasOwnIndex(["docs/index.adoc", "other.adoc"]));
    }

    [Fact]
    public void BuildIndexHtml_ListsPagesGroupedByFolder()
    {
        var html = WebsiteBuilder.BuildIndexHtml("My Docs",
        [
            new SitePage("readme.html", "Read Me"),
            new SitePage("docs/guide.html", "Guide"),
        ]);

        Assert.Contains("<title>My Docs</title>", html);
        Assert.Contains("""<a href="readme.html">Read Me</a>""", html);
        Assert.Contains("""<a href="docs/guide.html">Guide</a>""", html);
        Assert.Contains("<h2>docs</h2>", html);
    }

    [Fact]
    public void BuildIndexHtml_EscapesTitles()
    {
        var html = WebsiteBuilder.BuildIndexHtml("A & B",
            [new SitePage("x.html", "Tips <& tricks>")]);
        Assert.Contains("A &amp; B", html);
        Assert.Contains("Tips &lt;&amp; tricks&gt;", html);
    }
}
