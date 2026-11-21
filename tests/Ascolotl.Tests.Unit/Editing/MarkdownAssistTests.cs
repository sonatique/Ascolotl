namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing.Assist;

/// <summary>Cases mark the caret with ‸.</summary>
public sealed class MarkdownAssistTests
{
    private static AssistQuery At(string marked)
    {
        var caret = marked.IndexOf('‸');
        return MarkdownAssist.Analyze(marked.Remove(caret, 1), caret);
    }

    private static IReadOnlyList<AssistItem> Pool(string marked, string? folder = null)
    {
        var caret = marked.IndexOf('‸');
        var text = marked.Remove(caret, 1);
        return MarkdownAssist.PoolFor(MarkdownAssist.Analyze(text, caret), text, folder);
    }

    [Theory]
    [InlineData("‸", AssistContextKind.BlockStart, "")]
    [InlineData("ta‸", AssistContextKind.BlockStart, "ta")]
    [InlineData("```py‸", AssistContextKind.FenceLanguage, "py")]
    [InlineData("See [docs](img/lo‸", AssistContextKind.LinkTarget, "img/lo")]
    [InlineData("See [docs](#intr‸", AssistContextKind.Anchor, "intr")]
    [InlineData("See [docs][re‸", AssistContextKind.ReferenceLabel, "re")]
    [InlineData("Some prose‸", AssistContextKind.None, "")]
    [InlineData("See [docs](a.md) and more‸", AssistContextKind.None, "")]
    [InlineData("```\ncode [x](‸\n```", AssistContextKind.None, "")]
    [InlineData("```\ncode\n```‸", AssistContextKind.None, "")]
    public void Contexts(string marked, AssistContextKind kind, string word)
    {
        var q = At(marked);
        Assert.Equal((kind, word), (q.Kind, q.Word));
    }

    [Fact]
    public void Anchors_FollowGitHubSlugs()
    {
        var items = Pool("# Intro\n\n## Getting Started!\n\n## Getting Started!\n\n[x](#‸");
        Assert.Equal(["intro", "getting-started", "getting-started-1"], items.Select(i => i.Trigger));
        Assert.Equal("intro)", items[0].Snippet);
        Assert.Equal("intro", Pool("# Intro\n\n[x](#‸)")[0].Snippet); // the ) is already there
    }

    [Fact]
    public void ReferenceLabels_ComeFromDefinitions()
        => Assert.Equal(["docs", "home"], Pool("[docs]: https://a.org\n [home]: /\n\nSee [it][‸").Select(i => i.Trigger));

    [Fact]
    public void LinkTargets_ListTheFolder_AndImagesOnlyForImages()
    {
        var dir = Directory.CreateTempSubdirectory("ascolotl-md-files-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "img"));
            File.WriteAllText(Path.Combine(dir, "img", "logo.png"), "");
            File.WriteAllText(Path.Combine(dir, "my notes.md"), "");
            File.WriteAllText(Path.Combine(dir, ".hidden"), "");

            Assert.Equal(["img/", "my%20notes.md"], Pool("[a](‸", dir).Select(i => i.Trigger));
            Assert.Equal(["img/logo.png"], Pool("![a](img/‸", dir).Select(i => i.Trigger));
            Assert.Equal(["img/"], Pool("![a](‸", dir).Select(i => i.Trigger)); // no .md files for an image
            Assert.Empty(Pool("[a](‸", folder: null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FenceLanguages_AreOffered()
        => Assert.Contains(Pool("```‸"), i => i.Trigger == "csharp");

    [Theory]
    [InlineData("- item", "- ", false)]
    [InlineData("  * item", "  * ", false)]
    [InlineData("1. first", "2. ", false)]
    [InlineData("9) ninth", "10) ", false)]
    [InlineData("- [x] done", "- [ ] ", false)]
    [InlineData("> quoted", "> ", false)]
    [InlineData("> - nested", "> - ", false)]
    [InlineData("- ", "- ", true)]
    [InlineData("- [ ] ", "- [ ] ", true)]
    [InlineData("> ", "> ", true)]
    public void Enter_ContinuesListsAndQuotes(string line, string marker, bool ends)
        => Assert.Equal(new ListContinuation(marker, ends), MarkdownAssist.ContinueList(line));

    [Theory]
    [InlineData("Plain prose")]
    [InlineData("**bold** start")]
    [InlineData("-not a list")]
    [InlineData("")]
    public void Enter_LeavesOtherLinesAlone(string line) => Assert.Null(MarkdownAssist.ContinueList(line));

    [Fact]
    public void Enter_ClosesAnOpenedFenceOnly()
    {
        Assert.Equal("```", MarkdownAssist.ClosingFenceFor("```js", "Text\n```js"));
        Assert.Null(MarkdownAssist.ClosingFenceFor("```", "```js\ncode\n```"));
        Assert.Equal("~~~", MarkdownAssist.ClosingFenceFor("~~~", "~~~"));
    }
}
