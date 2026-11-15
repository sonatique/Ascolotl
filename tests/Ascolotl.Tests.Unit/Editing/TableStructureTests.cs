namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="TableStructure"/> — row/column source edits for
/// simple AsciiDoc tables.
/// </summary>
public class TableStructureTests
{
    [Fact]
    public void AdjustCols_QuotedSpecs_Grow()
    {
        Assert.Equal("[cols=\"1,2,1\"]", TableStructure.AdjustColsAttribute("[cols=\"1,2\"]", 1));
    }

    [Fact]
    public void AdjustCols_QuotedSpecs_Shrink()
    {
        Assert.Equal("[cols=\"1,2\"]", TableStructure.AdjustColsAttribute("[cols=\"1,2,1\"]", -1));
    }

    [Fact]
    public void AdjustCols_NumericForm()
    {
        Assert.Equal("[cols=4]", TableStructure.AdjustColsAttribute("[cols=3]", 1));
        Assert.Equal("[cols=2]", TableStructure.AdjustColsAttribute("[cols=3]", -1));
    }

    [Fact]
    public void AdjustCols_NeverDropsBelowOne()
    {
        Assert.Equal("[cols=1]", TableStructure.AdjustColsAttribute("[cols=1]", -5));
        Assert.Equal("[cols=\"1\"]", TableStructure.AdjustColsAttribute("[cols=\"1,1\"]", -5));
    }

    [Fact]
    public void AdjustCols_NotAColsLine_ReturnsNull()
    {
        Assert.Null(TableStructure.AdjustColsAttribute("[options=\"header\"]", 1));
        Assert.Null(TableStructure.AdjustColsAttribute("| a | b", 1));
    }

    // ── FlatCellIndex ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, 0)] // first cell of first row
    [InlineData(0, 2, 2)] // third cell of first row
    [InlineData(1, 0, 3)] // first cell of second row (after row 0's 3 cells)
    [InlineData(2, 1, 6)] // second cell of third row (3 + 2 + 1)
    public void FlatCellIndex_ThreeByVarying(int row, int cell, int expected)
    {
        // Rows with 3, 2, 3 cells.
        Assert.Equal(expected, TableStructure.FlatCellIndex(new[] { 3, 2, 3 }, row, cell));
    }

    [Fact]
    public void FlatCellIndex_RoundTripsWalkBack()
    {
        // Flattening then walking the flat index back (as TryResolveCell does)
        // must recover the original (row, cell).
        int[] cellsPerRow = { 2, 4, 1, 3 };
        for (var r = 0; r < cellsPerRow.Length; r++)
            for (var c = 0; c < cellsPerRow[r]; c++)
            {
                var flat = TableStructure.FlatCellIndex(cellsPerRow, r, c)!.Value;
                var rr = 0;
                var idx = flat;
                while (idx >= cellsPerRow[rr]) { idx -= cellsPerRow[rr]; rr++; }
                Assert.Equal((r, c), (rr, idx));
            }
    }

    [Theory]
    [InlineData(-1, 0)] // negative row
    [InlineData(3, 0)]  // row out of range
    [InlineData(0, -1)] // negative cell
    [InlineData(0, 3)]  // cell out of range for that row
    public void FlatCellIndex_OutOfRange_ReturnsNull(int row, int cell)
    {
        Assert.Null(TableStructure.FlatCellIndex(new[] { 3, 2 }, row, cell));
    }
}
