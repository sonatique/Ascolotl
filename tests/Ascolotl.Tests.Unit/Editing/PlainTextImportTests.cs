namespace Ascolotl.Tests.Unit.Editing;

using AdocNet.Ast;
using AdocNet.Parser;
using Ascolotl.Core.Editing;

public sealed class PlainTextImportTests
{
    [Fact]
    public void TitleAndParagraphs()
    {
        var adoc = PlainTextImport.ToAsciiDoc("First line\nsame paragraph\r\n\r\n\r\nSecond paragraph", "Notes");

        Assert.Equal("= Notes\n\nFirst line\nsame paragraph\n\nSecond paragraph\n", adoc);
    }

    [Fact]
    public void ListsBecomeAsciiDocLists()
    {
        var adoc = PlainTextImport.ToAsciiDoc("Shopping:\n- milk\n* bread\n• eggs\nThanks\n\n1. one\n2) two", "T");

        Assert.Equal("= T\n\nShopping:\n\n* milk\n* bread\n* eggs\n\nThanks\n\n. one\n. two\n", adoc);
        var blocks = AdocParser.Parse(adoc).Document.Children;
        Assert.Equal(4, blocks.Count); // paragraph, bullet list, paragraph, numbered list
        Assert.IsAssignableFrom<ListNode>(blocks[1]);
        Assert.IsAssignableFrom<ListNode>(blocks[3]);
    }

    [Fact]
    public void MarkupLookalikesStayText()
    {
        var adoc = PlainTextImport.ToAsciiDoc("== not a heading\n.not a title\n[not attrs]\n|not a table", "T");
        var doc = AdocParser.Parse(adoc).Document;

        Assert.Equal("T", doc.Title);
        Assert.Single(doc.Children); // one paragraph: no section, block title, attribute list or table
        Assert.Contains("{empty}== not a heading", adoc);
    }

    [Fact]
    public void IndentedBlockStaysLiteral()
    {
        var adoc = PlainTextImport.ToAsciiDoc("Code:\n\n    x = 1\n    y = 2\n", "T");

        Assert.Contains("\n    x = 1\n    y = 2\n", adoc);
    }
}
