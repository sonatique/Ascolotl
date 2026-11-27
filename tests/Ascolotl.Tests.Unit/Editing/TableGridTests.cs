namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="TableGrid"/> — the span-aware table model: parsing,
/// serialisation, and structural edits that keep col/row spans consistent.
/// </summary>
public class TableGridTests
{
    private static TableGrid Parse(params string[] lines) =>
        TableGrid.Parse(lines) ?? throw new Xunit.Sdk.XunitException("Parse returned null");

    // ---- Parse / serialise round-trips ----

    [Fact]
    public void Parse_Simple_RoundTrips()
    {
        var g = Parse("| a | b", "| c | d");
        Assert.Equal(2, g.ColumnCount);
        Assert.Equal(new[] { "| a | b", "| c | d" }, g.ToLines());
    }

    [Theory]
    [InlineData("|1|2|3", "| 1 | 2 | 3")]
    [InlineData("| Version 2| Notes", "| Version 2 | Notes")]
    [InlineData("| 10| 20", "| 10 | 20")]
    [InlineData("|Name|Value", "| Name | Value")]
    public void Parse_TextRightBeforeAPipe_IsContent(string row, string written)
    {
        var g = Parse(row);
        Assert.Equal(new[] { written }, g.ToLines());
    }

    [Theory]
    [InlineData("| a 2+^| b", 3, "| a 2+^| b")]
    [InlineData("|2+| a", 3, "| 2+| a")]
    [InlineData("| a h| b", 2, "| a h| b")]
    public void Parse_SpecAfterWhitespaceOrFillingItsSegment_IsASpec(string row, int columns, string written)
    {
        var g = Parse(row);
        Assert.Equal(columns, g.ColumnCount);
        Assert.Equal(new[] { written }, g.ToLines());
    }

    [Fact]
    public void Parse_ColSpan_CountsColumns()
    {
        var g = Parse("| a 2+| b");
        Assert.Equal(3, g.ColumnCount);
        Assert.Equal(new[] { "| a 2+| b" }, g.ToLines());
    }

    [Fact]
    public void Parse_RowSpan_RoundTrips()
    {
        var g = Parse(".2+| a | b", "| d");
        Assert.Equal(2, g.ColumnCount);
        Assert.Equal(new[] { ".2+| a | b", "| d" }, g.ToLines());
    }

    [Fact]
    public void Parse_Multiplier_Bails()
    {
        Assert.Null(TableGrid.Parse(new[] { "3*| a" }));
    }

    [Fact]
    public void Parse_OneCellPerLine_Bails()
    {
        // Rows spread over several lines: the line-per-row model would delete
        // single cells, so the caller must fall back to editing the source.
        Assert.Null(TableGrid.Parse(new[] { "| a | b", "| c", "| d" }));
        Assert.Null(TableGrid.Parse(new[] { "| a", "| b", "| c", "| d" }, columnCount: 2));
    }

    [Fact]
    public void Parse_OverlappingOrOverhangingSpans_Bails()
    {
        Assert.Null(TableGrid.Parse(new[] { "| a .2+| b", "| c | d" }));  // row 1 overfills
        Assert.Null(TableGrid.Parse(new[] { "| a .2+| b" }));             // span past last row
    }

    [Fact]
    public void ImplicitHeaderBlankLine_SurvivesRowEdits()
    {
        var g = Parse("| h1 | h2", "", "| a | b", "| c | d");
        Assert.True(g.DeleteRow(2));
        Assert.Equal(new[] { "| h1 | h2", "", "| a | b" }, g.ToLines());
    }

    [Fact]
    public void Parse_PreservesAlignmentSpec()
    {
        var g = Parse("| a ^| b");
        Assert.Equal(new[] { "| a ^| b" }, g.ToLines());
    }

    // ---- Merge ----

    [Fact]
    public void MergeRight_Middle()
    {
        var g = Parse("| a | b | c");
        Assert.True(g.MergeRight(0, 1));
        Assert.Equal(new[] { "| a 2+| b" }, g.ToLines());
        Assert.Equal(3, g.ColumnCount);
    }

    [Fact]
    public void MergeRight_NoNeighbour_False()
    {
        var g = Parse("| a | b");
        Assert.False(g.MergeRight(0, 1));
    }

    [Fact]
    public void MergeRight_PastARowSpanFromAbove_False()
    {
        var g = Parse("| a .2+| b | c", "| d | e"); // d in column 0, e in column 2: b holds column 1
        Assert.False(g.MergeRight(1, 0));
        Assert.Equal(new[] { "| a .2+| b | c", "| d | e" }, g.ToLines());
    }

    [Fact]
    public void MergeDown_GrowsRowSpanAndDropsBelow()
    {
        var g = Parse("| a | b", "| c | d");
        Assert.True(g.MergeDown(0, 0));
        Assert.Equal(new[] { ".2+| a | b", "| d" }, g.ToLines());
    }

    [Fact]
    public void Merge_ThenMergeAgain_StillWorks()
    {
        // The whole point: merging keeps working on an already-merged table.
        var g = Parse("| a | b | c", "| d | e | f");
        Assert.True(g.MergeRight(0, 0));          // a spans cols 0-1
        Assert.True(g.MergeRight(0, 0));          // a spans cols 0-2
        Assert.Equal(new[] { "3+| a", "| d | e | f" }, g.ToLines());
    }

    // ---- Split ----

    [Fact]
    public void Split_ColSpan_RestoresEmptyCell()
    {
        var g = Parse("| a 2+| b");   // b spans cols 1-2
        Assert.True(g.Split(0, 1));
        Assert.Equal(new[] { "| a | b |" }, g.ToLines());
    }

    [Fact]
    public void Split_RowSpan_RestoresCellBelow()
    {
        var g = Parse(".2+| a | b", "| d");
        Assert.True(g.Split(0, 0));
        Assert.Equal(new[] { "| a | b", "| | d" }, g.ToLines());
    }

    [Fact]
    public void Split_Unspanned_False()
    {
        var g = Parse("| a | b");
        Assert.False(g.Split(0, 0));
    }

    // ---- Rows ----

    [Fact]
    public void InsertRow_Below_AddsBlankRow()
    {
        var g = Parse("| a | b", "| c | d");
        Assert.True(g.InsertRow(0, below: true));
        Assert.Equal(new[] { "| a | b", "| |", "| c | d" }, g.ToLines());
    }

    [Fact]
    public void InsertRow_ExtendsCrossingRowSpan()
    {
        var g = Parse(".2+| a | b", "| d");      // a spans rows 0-1 in col 0
        Assert.True(g.InsertRow(0, below: true)); // new row falls inside the span
        Assert.Equal(new[] { ".3+| a | b", "|", "| d" }, g.ToLines());
    }

    [Fact]
    public void DeleteRow_ShrinksCrossingRowSpan()
    {
        var g = Parse(".2+| a | b", "| d");
        Assert.True(g.DeleteRow(1));
        Assert.Equal(new[] { "| a | b" }, g.ToLines());
    }

    [Fact]
    public void DeleteRow_OnlyRow_False()
    {
        var g = Parse("| a | b");
        Assert.False(g.DeleteRow(0));
    }

    // ---- Columns ----

    [Fact]
    public void InsertColumn_Right_AddsCellEveryRow()
    {
        var g = Parse("| a | b", "| c | d");
        Assert.True(g.InsertColumn(0, 1, right: true));
        Assert.Equal(3, g.ColumnCount);
        Assert.Equal(new[] { "| a | b |", "| c | d |" }, g.ToLines());
    }

    [Fact]
    public void InsertColumn_Left()
    {
        var g = Parse("| a | b");
        Assert.True(g.InsertColumn(0, 1, right: false));
        Assert.Equal(new[] { "| a | | b" }, g.ToLines());
    }

    [Fact]
    public void DeleteColumn_RemovesCellEveryRow()
    {
        var g = Parse("| a | b | c", "| d | e | f");
        Assert.True(g.DeleteColumn(0, 1));
        Assert.Equal(new[] { "| a | c", "| d | f" }, g.ToLines());
    }

    [Fact]
    public void DeleteColumn_ShrinksColSpan()
    {
        var g = Parse("| a 2+| b");
        Assert.True(g.DeleteColumn(0, 1));        // remove one of b's columns
        Assert.Equal(new[] { "| a | b" }, g.ToLines());
    }

    [Fact]
    public void ColumnOps_BailOnRowSpans()
    {
        var g = Parse(".2+| a | b", "| d");
        Assert.False(g.InsertColumn(0, 1, right: true));
        Assert.False(g.DeleteColumn(0, 1));
    }
}
