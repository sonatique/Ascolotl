namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="BlockInsertion"/> — splicing a new block at a
/// boundary with correct blank-line separation.
/// </summary>
public class BlockInsertionTests
{
    [Fact]
    public void Join_BothNonEmpty_OneBlankLine()
    {
        Assert.Equal("a\n\nb", BlockInsertion.Join("a", "b"));
    }

    [Fact]
    public void Join_CollapsesExistingNewlinesAtSeam()
    {
        Assert.Equal("a\n\nb", BlockInsertion.Join("a\n\n\n", "\n\nb"));
    }

    [Fact]
    public void Join_EmptySide_ReturnsOther()
    {
        Assert.Equal("b", BlockInsertion.Join("", "b"));
        Assert.Equal("a", BlockInsertion.Join("a", ""));
    }

    [Fact]
    public void InsertAfter_BetweenTwoBlocks()
    {
        // "Para one." ends at offset 9; insert a heading after it.
        var src = "Para one.\n\nPara two.\n";
        var result = BlockInsertion.InsertAfter(src, 9, "== Heading");
        Assert.Equal("Para one.\n\n== Heading\n\nPara two.\n", result);
    }

    [Fact]
    public void InsertBefore_BetweenTwoBlocks()
    {
        // "Para two." starts at offset 11.
        var src = "Para one.\n\nPara two.\n";
        var result = BlockInsertion.InsertBefore(src, 11, "== Heading");
        Assert.Equal("Para one.\n\n== Heading\n\nPara two.\n", result);
    }

    [Fact]
    public void InsertAfter_LastBlock_KeepsTrailingNewline()
    {
        var src = "Only block.\n";
        var result = BlockInsertion.InsertAfter(src, 11, "New paragraph.");
        Assert.Equal("Only block.\n\nNew paragraph.", result);
    }

    [Fact]
    public void Append_ToDocument()
    {
        Assert.Equal("Doc.\n\n== Tail", BlockInsertion.Append("Doc.\n", "== Tail"));
    }

    [Fact]
    public void Append_ToEmptyDocument_IsJustTheSnippet()
    {
        Assert.Equal("== Heading", BlockInsertion.Append("", "== Heading"));
    }

    [Fact]
    public void InsertAfter_MultiLineSnippet_Preserved()
    {
        var src = "Intro.\n";
        var result = BlockInsertion.InsertAfter(src, 6, "* a\n* b");
        Assert.Equal("Intro.\n\n* a\n* b", result);
    }

    [Fact]
    public void Snippets_HeadingLevels()
    {
        Assert.Equal("== New heading", BlockSnippets.Heading(1));
        Assert.Equal("=== New heading", BlockSnippets.Heading(2));
        Assert.Equal("==== New heading", BlockSnippets.Heading(3));
    }

    [Fact]
    public void Snippets_TableIsParseableShape()
    {
        Assert.StartsWith("[cols=", BlockSnippets.Table);
        Assert.Contains("|===", BlockSnippets.Table);
    }
}
