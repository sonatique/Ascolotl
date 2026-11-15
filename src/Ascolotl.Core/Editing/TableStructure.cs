namespace Ascolotl.Core.Editing;

/// <summary>
/// Pure string operations for adding/removing rows and columns of a
/// <em>simple</em> AsciiDoc table (one source line per row, no row- or
/// column-spans). The caller (which has the parsed layout) is responsible
/// for deciding a table is simple and for locating each row's source line;
/// these helpers only manipulate the row-line text and the optional
/// <c>cols=</c> attribute.
///
/// <para>Spanned/multi-line tables are deliberately out of scope: inserting
/// a column into a table with rowspans requires re-deriving the whole grid,
/// which is the parser's job — the WYSIWYG UI falls back to "Edit table
/// source" for those.</para>
/// </summary>
public static class TableStructure
{
    /// <summary>
    /// Adjust a <c>[cols="…"]</c> attribute line by <paramref name="delta"/>
    /// columns: appends <c>,1</c> specs when growing, drops trailing specs when
    /// shrinking. Returns null when the line isn't a recognised cols attribute
    /// (the caller then leaves it alone — AsciiDoc infers columns from the
    /// first row when no spec is present).
    /// </summary>
    public static string? AdjustColsAttribute(string attrLine, int delta)
    {
        var trimmed = attrLine.Trim();
        // Accept [cols="1,1"] or [cols=2] etc.
        if (!trimmed.StartsWith("[cols=", StringComparison.OrdinalIgnoreCase)
            || !trimmed.EndsWith("]", StringComparison.Ordinal))
            return null;

        var inner = trimmed["[cols=".Length..^1].Trim().Trim('"');

        // Numeric form: [cols=3] → just change the number.
        if (int.TryParse(inner, out var count))
        {
            var newCount = Math.Max(1, count + delta);
            return $"[cols={newCount}]";
        }

        var specs = inner.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).ToList();
        if (specs.Count == 0) return null;

        if (delta > 0)
            specs.AddRange(Enumerable.Repeat("1", delta));
        else if (delta < 0)
            specs = specs.Take(Math.Max(1, specs.Count + delta)).ToList();

        return $"[cols=\"{string.Join(",", specs)}\"]";
    }

    /// <summary>
    /// Flatten a (row, column) cell address into its index in a row-major cell
    /// list (the cells of all rows concatenated). <paramref name="cellsPerRow"/>
    /// gives each row's cell count. Returns null if the address is out of range.
    /// This is the inverse of walking a flat grid-child index back to a cell —
    /// used to re-open a clicked cell by address after a table re-renders.
    /// </summary>
    public static int? FlatCellIndex(IReadOnlyList<int> cellsPerRow, int rowIndex, int cellIndex)
    {
        if (rowIndex < 0 || rowIndex >= cellsPerRow.Count) return null;
        if (cellIndex < 0 || cellIndex >= cellsPerRow[rowIndex]) return null;

        var flat = 0;
        for (var i = 0; i < rowIndex; i++) flat += cellsPerRow[i];
        return flat + cellIndex;
    }
}
