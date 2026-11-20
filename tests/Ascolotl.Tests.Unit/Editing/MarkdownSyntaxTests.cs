namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;
using static Ascolotl.Core.Editing.MarkdownSyntax;

public sealed class MarkdownSyntaxTests
{
    private static AsciiDocSyntax.Span Only(string line) => Assert.Single(Classify(line));

    private static IEnumerable<(string Text, SyntaxKind Kind)> Coloured(string line)
        => Classify(line).Select(s => (line.Substring(s.Start, s.Length), s.Kind));

    [Theory]
    [InlineData("# Title", SyntaxKind.Heading)]
    [InlineData("### Section", SyntaxKind.Heading)]
    [InlineData("#", SyntaxKind.Heading)]
    [InlineData("<!-- a comment -->", SyntaxKind.Comment)]
    [InlineData("```csharp", SyntaxKind.Delimiter)]
    [InlineData("~~~", SyntaxKind.Delimiter)]
    [InlineData("---", SyntaxKind.Delimiter)]
    [InlineData("* * *", SyntaxKind.Delimiter)]
    [InlineData("===", SyntaxKind.Delimiter)]
    [InlineData("| --- | :---: |", SyntaxKind.Delimiter)]
    [InlineData("---|--:", SyntaxKind.Delimiter)]
    [InlineData("[docs]: https://example.org", SyntaxKind.Macro)]
    public void WholeLineConstructs(string line, SyntaxKind kind)
    {
        var span = Only(line);
        Assert.Equal((0, line.Length, kind), (span.Start, span.Length, span.Kind));
    }

    [Theory]
    [InlineData("#hashtag")]
    [InlineData("**bold** and _italic_")]
    [InlineData("| a | b |")]
    [InlineData("Plain text with no markup")]
    public void PlainLines_StayPlain(string line) => Assert.Empty(Classify(line));

    [Fact]
    public void ListMarker_ThenCodeAndLinks()
        => Assert.Equal(
            [("-", SyntaxKind.ListMarker), ("`a [b](c)`", SyntaxKind.Monospace),
             ("[the site](https://example.org)", SyntaxKind.Macro), ("<https://x.org>", SyntaxKind.Macro)],
            Coloured("- see `a [b](c)` and [the site](https://example.org) or <https://x.org>"));

    [Theory]
    [InlineData("1. item", "1.")]
    [InlineData("  2) item", "2)")]
    [InlineData("- [ ] to do", "- [ ]")]
    [InlineData("* [x] done", "* [x]")]
    [InlineData("> quoted", ">")]
    [InlineData("> > - nested", "> > -")]
    public void Markers(string line, string marker)
        => Assert.Equal((marker, SyntaxKind.ListMarker), Coloured(line).First());

    [Fact]
    public void Alert_ColoursLabel()
        => Assert.Equal([(">", SyntaxKind.ListMarker), ("[!NOTE]", SyntaxKind.Admonition)], Coloured("> [!NOTE]"));

    [Fact]
    public void DoubleBacktickCode_HoldsASingleBacktick()
        => Assert.Equal([("``a ` b``", SyntaxKind.Monospace)], Coloured("x ``a ` b`` y"));

    [Fact]
    public void ImageAndReferenceLink_AreLinks()
        => Assert.Equal(
            [("![logo](a.png)", SyntaxKind.Macro), ("[guide][g]", SyntaxKind.Macro), ("https://e.org/p", SyntaxKind.Macro)],
            Coloured("![logo](a.png) the [guide][g] at https://e.org/p."));
}
