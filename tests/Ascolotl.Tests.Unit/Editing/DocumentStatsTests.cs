namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="DocumentStats"/> — reader-facing word counting that
/// skips AsciiDoc structural lines.
/// </summary>
public class DocumentStatsTests
{
    [Fact]
    public void Empty_IsZero()
    {
        var s = DocumentStats.Count("");
        Assert.Equal(0, s.Words);
        Assert.Equal(0, s.Characters);
        Assert.Equal(0, s.ReadingMinutes);
        Assert.Equal(0, DocumentStats.Count(null).Words);
    }

    [Fact]
    public void PlainProse_CountsWords()
    {
        Assert.Equal(4, DocumentStats.Count("The quick brown fox").Words);
    }

    [Fact]
    public void InlineMarkup_DoesNotSplitWords()
    {
        // *bold*, _italic_, `mono`, don't — each one word.
        Assert.Equal(4, DocumentStats.Count("*bold* _italic_ `mono` don't").Words);
    }

    [Fact]
    public void HeadingsCount_MarkersDoNot()
    {
        Assert.Equal(2, DocumentStats.Count("== Getting Started").Words);
    }

    [Fact]
    public void ListItems_CountContentOnly()
    {
        Assert.Equal(4, DocumentStats.Count("* first item\n. second item").Words);
    }

    [Theory]
    [InlineData("// a comment nobody reads")]
    [InlineData(":toc: left")]
    [InlineData("[source,java]")]
    [InlineData("[[anchor]]")]
    [InlineData("----")]
    [InlineData("====")]
    [InlineData("|===")]
    [InlineData("'''")]
    public void StructuralLines_CountZero(string line)
    {
        Assert.Equal(0, DocumentStats.Count(line).Words);
    }

    [Fact]
    public void TableCells_CountContent()
    {
        Assert.Equal(2, DocumentStats.Count("|===\n| alpha | beta\n|===").Words);
    }

    [Fact]
    public void MixedDocument_CountsProseOnly()
    {
        var doc = "= Title\n:author: Me\n\nSome prose here.\n\n----\ncode inside\n----\n";
        // "Title" (1) + "Some prose here." (3). Verbatim content? Fence lines are
        // skipped but the code line itself counts as prose in v1 — accepted:
        // "code inside" (2). Total 6.
        Assert.Equal(6, DocumentStats.Count(doc).Words);
    }

    [Fact]
    public void ReadingTime_RoundsAndFloorsAtOneMinute()
    {
        Assert.Equal(1, DocumentStats.Count("word").ReadingMinutes);
        var fiveHundred = string.Join(' ', Enumerable.Repeat("word", 500));
        Assert.Equal(3, DocumentStats.Count(fiveHundred).ReadingMinutes); // 500/200 → 2.5 → 3
    }
}
