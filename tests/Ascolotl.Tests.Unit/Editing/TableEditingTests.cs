namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>Cases mark the caret with ‸; results mark where it ends up.</summary>
public sealed class TableEditingTests
{
    private static string? Run(TableOp op, bool markdown, string marked)
    {
        var caret = marked.IndexOf('‸');
        var text = marked.Remove(caret, 1);
        if (TableEditing.Apply(op, text, caret, markdown) is not { } edit) return null;
        var result = text[..edit.Start] + edit.Text + text[(edit.Start + edit.Length)..];
        return result.Insert(edit.Caret, "‸");
    }

    private const string Adoc = "[cols=\"1,1\"]\n|===\n| A | B\n\n| a1 | b1\n| a2 | b2\n|===\n";

    private static string AdocAt(string cell) => Adoc.Replace(cell, "‸" + cell);

    [Fact]
    public void AsciiDoc_RowBelow_PutsTheCaretInIt()
        => Assert.Equal("[cols=\"1,1\"]\n|===\n| A | B\n\n| a1 | b1\n| ‸|\n| a2 | b2\n|===\n",
            Run(TableOp.RowBelow, false, AdocAt("a1")));

    [Fact]
    public void AsciiDoc_RowAbove()
        => Assert.Equal("[cols=\"1,1\"]\n|===\n| A | B\n\n| a1 | b1\n| ‸|\n| a2 | b2\n|===\n",
            Run(TableOp.RowAbove, false, AdocAt("a2")));

    [Fact]
    public void AsciiDoc_ColumnRight_WidensTheColsAttribute()
        => Assert.Equal("[cols=\"1,1,1\"]\n|===\n| A | B |\n\n| a1 | b1 |‸\n| a2 | b2 |\n|===\n",
            Run(TableOp.ColumnRight, false, AdocAt("b1")));

    [Fact]
    public void AsciiDoc_DeleteColumn()
        => Assert.Equal("[cols=\"1\"]\n|===\n| ‸B\n\n| b1\n| b2\n|===\n",
            Run(TableOp.DeleteColumn, false, AdocAt("A")));

    [Fact]
    public void AsciiDoc_DeleteRow_MovesUp()
        => Assert.Equal("[cols=\"1,1\"]\n|===\n| A | B\n\n| ‸a1 | b1\n|===\n",
            Run(TableOp.DeleteRow, false, AdocAt("a2")));

    [Fact]
    public void AsciiDoc_MergeRight_SpansTheCell()
        => Assert.Contains("2+|", Run(TableOp.MergeRight, false, AdocAt("a1")));

    [Fact]
    public void AsciiDoc_KeepsCrlf()
        => Assert.Equal("|===\r\n| a | b\r\n| ‸|\r\n|===",
            Run(TableOp.RowBelow, false, "|===\r\n| a‸ | b\r\n|==="));

    [Theory]
    [InlineData("Before.\n\n‸|===\n| a\n|===")]
    [InlineData("|===\n| a\n|===\n\nAfter‸.")]
    [InlineData("|===\n| a\n\n‸\n| b\n|===")]
    [InlineData("Text with a | pipe‸.")]
    public void AsciiDoc_OutsideATableRow_DoesNothing(string marked)
    {
        Assert.Null(Run(TableOp.RowBelow, false, marked));
        var caret = marked.IndexOf('‸');
        Assert.False(TableEditing.IsInTable(marked.Remove(caret, 1), caret, markdown: false));
    }

    private const string Md = "Intro.\n\n| Name | Age |\n| --- | ---: |\n| Ada | 3 |\n\nAfter.";

    [Fact]
    public void Markdown_RowBelow_AlignsTheTable()
        => Assert.Equal("Intro.\n\n| Name | Age |\n| ---- | --: |\n| Ada  | 3   |\n| ‸     |     |\n\nAfter.",
            Run(TableOp.RowBelow, true, Md.Replace("Ada", "A‸da")));

    [Fact]
    public void Markdown_RowBelowTheHeader_IsTheFirstBodyRow()
        => Assert.Equal("Intro.\n\n| Name | Age |\n| ---- | --: |\n| ‸     |     |\n| Ada  | 3   |\n\nAfter.",
            Run(TableOp.RowBelow, true, Md.Replace("Name", "‸Name")));

    [Fact]
    public void Markdown_ColumnLeft_KeepsTheOtherAlignment()
        => Assert.Equal("Intro.\n\n| Name | ‸    | Age |\n| ---- | --- | --: |\n| Ada  |     | 3   |\n\nAfter.",
            Run(TableOp.ColumnLeft, true, Md.Replace("Age", "A‸ge")));

    [Fact]
    public void Markdown_DeleteColumn()
        => Assert.Equal("Intro.\n\n| ‸Age |\n| --: |\n| 3   |\n\nAfter.",
            Run(TableOp.DeleteColumn, true, Md.Replace("Name", "Na‸me")));

    [Theory]
    [InlineData(TableOp.RowAbove)]
    [InlineData(TableOp.DeleteRow)]
    [InlineData(TableOp.MergeRight)]
    public void Markdown_RefusesWhatItCantDo(TableOp op)
        => Assert.Null(Run(op, true, Md.Replace("Name", "‸Name")));

    [Fact]
    public void Markdown_KeepsCellsPastTheHeader()
        => Assert.Contains("| x   |", Run(TableOp.RowBelow, true, "| a |\n| --- |\n| b‸ | x |"));

    [Fact]
    public void Markdown_PipeProseIsNotATable()
        => Assert.False(TableEditing.IsInTable("a | b\n\nc | d", 1, markdown: true));
}
