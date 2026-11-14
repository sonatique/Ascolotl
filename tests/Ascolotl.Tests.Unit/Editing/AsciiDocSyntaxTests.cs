namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;
using static Ascolotl.Core.Editing.AsciiDocSyntax;

public sealed class AsciiDocSyntaxTests
{
    private static Span Only(string line) => Assert.Single(Classify(line));

    [Theory]
    [InlineData("= Title", SyntaxKind.Heading)]
    [InlineData("=== Section", SyntaxKind.Heading)]
    [InlineData("// a comment", SyntaxKind.Comment)]
    [InlineData("----", SyntaxKind.Delimiter)]
    [InlineData("|===", SyntaxKind.Delimiter)]
    [InlineData("--", SyntaxKind.Delimiter)]
    [InlineData(".Block title", SyntaxKind.BlockTitle)]
    public void WholeLineConstructs(string line, SyntaxKind kind)
    {
        var span = Only(line);
        Assert.Equal((0, line.Length, kind), (span.Start, span.Length, span.Kind));
    }

    [Fact]
    public void AttributeEntry_ColoursNameOnly()
        => Assert.Equal(new Span(0, 5, SyntaxKind.Attribute), Only(":toc: macro"));

    [Theory]
    [InlineData("=no space")]
    [InlineData("...")] // an ellipsis, not a literal-block delimiter
    [InlineData("Plain text with no markup")]
    public void PlainLines_StayPlain(string line) => Assert.Empty(Classify(line));

    [Fact]
    public void ListMarker_ThenInlineMacroAndMonospace()
    {
        var spans = Classify("* see link:a.adoc[A] and `code` <<intro>>");

        Assert.Equal(
            [SyntaxKind.ListMarker, SyntaxKind.Macro, SyntaxKind.Monospace, SyntaxKind.Macro],
            spans.Select(s => s.Kind));
        Assert.Equal(new Span(0, 1, SyntaxKind.ListMarker), spans[0]);
        Assert.Equal("link:a.adoc[A]", "* see link:a.adoc[A] and `code` <<intro>>".Substring(spans[1].Start, spans[1].Length));
    }

    [Fact]
    public void Admonition_ColoursLabel()
        => Assert.Equal(new Span(0, 5, SyntaxKind.Admonition), Classify("NOTE: careful")[0]);

    [Fact]
    public void UrlWithLinkText_IsOneMacro()
    {
        const string line = "Go to https://example.org[the site] now";
        var span = Only(line);
        Assert.Equal("https://example.org[the site]", line.Substring(span.Start, span.Length));
    }
}
