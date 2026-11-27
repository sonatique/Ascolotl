namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="TableStructure"/> — row/column source edits for
/// simple AsciiDoc tables.
/// </summary>
public class TableStructureTests
{
    [Theory]
    [InlineData("[cols=\"1,2\"]", 2, 1, "[cols=\"1,2,1\"]")]
    [InlineData("[cols=\"2,3\"]", 0, 1, "[cols=\"1,2,3\"]")]
    [InlineData("[cols=\"1,2,1\"]", 2, -1, "[cols=\"1,2\"]")]
    [InlineData("[cols=\"1h,3a\"]", 0, -1, "[cols=\"3a\"]")]
    [InlineData("[cols=3]", 1, 1, "[cols=4]")]
    [InlineData("[cols=3]", 1, -1, "[cols=2]")]
    [InlineData("[cols=1]", 0, -1, "[cols=1]")]
    [InlineData("[cols=\"1\"]", 0, -1, "[cols=\"1\"]")]
    [InlineData("[cols=\"3*\"]", 1, -1, "[cols=\"1,1\"]")]
    [InlineData("[cols=\"2*^,1\"]", 3, 1, "[cols=\"^,^,1,1\"]")]
    [InlineData("[cols=\"1,2\",options=\"header\"]", 2, 1, "[cols=\"1,2,1\",options=\"header\"]")]
    [InlineData("[cols=\"1,2\",options=\"header\"]", 1, -1, "[cols=\"1\",options=\"header\"]")]
    [InlineData("[%header,cols=\"1,2\"]", 0, -1, "[%header,cols=\"2\"]")]
    [InlineData("[cols=3,options=\"header\"]", 3, 1, "[cols=4,options=\"header\"]")]
    [InlineData("[cols='1,2']\r", 2, 1, "[cols='1,2,1']\r")]
    public void AdjustCols_ChangesOnlyTheColsValue_AtTheColumn(string line, int column, int delta, string expected)
        => Assert.Equal(expected, TableStructure.AdjustColsAttribute(line, column, delta));

    [Theory]
    [InlineData("[options=\"header\"]")]
    [InlineData("| a | b")]
    [InlineData("[subcols=\"1,2\"]")]
    public void AdjustCols_NoColsValue_ReturnsNull(string line)
        => Assert.Null(TableStructure.AdjustColsAttribute(line, 0, 1));

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

    // ── CellAcross ────────────────────────────────────────────────────────

    // | A A | B |      A spans two columns
    // | C | D | E |
    // | F | G | H |    F spans two rows
    // | F | I | J |
    // | K K K |        K spans three columns
    private static readonly IReadOnlyList<IReadOnlyList<(int, int)>> Spans =
    [
        [(2, 1), (1, 1)],
        [(1, 1), (1, 1), (1, 1)],
        [(1, 2), (1, 1), (1, 1)],
        [(1, 1), (1, 1)],
        [(3, 1)],
    ];

    [Theory]
    [InlineData(1, 1, -1, 0, 0)] // D up: A, which spans over it
    [InlineData(0, 1, 1, 1, 2)]  // B down: E, its column
    [InlineData(1, 2, -1, 0, 1)] // E up: B
    [InlineData(3, 0, -1, 2, 1)] // I up: G, not F beside it
    [InlineData(3, 1, -1, 2, 2)] // J up: H
    [InlineData(2, 0, 1, 4, 0)]  // F down: past its own second row, K
    [InlineData(4, 0, -1, 2, 0)] // K up: F, from the row it starts in
    [InlineData(2, 1, 1, 3, 0)]  // G down: I
    public void CellAcross_KeepsTheColumnAcrossSpans(int row, int cell, int step, int toRow, int toCell) =>
        Assert.Equal((toRow, toCell), TableStructure.CellAcross(Spans, (row, cell), step));

    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(4, 0, 1)]
    [InlineData(5, 0, -1)] // no such row
    [InlineData(3, 2, -1)] // no such cell
    public void CellAcross_PastTheEdgeOrFromNoCell_IsNull(int row, int cell, int step) =>
        Assert.Null(TableStructure.CellAcross(Spans, (row, cell), step));
}
