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
    /// An attribute line with its <c>cols</c> value following a column inserted
    /// (<paramref name="delta"/> 1) or deleted (-1) at <paramref name="column"/>: a count changes
    /// by one; a spec list gets a <c>1</c> there or loses that column's spec, with its
    /// <c>N*</c> repeats written out. The rest of the line stays as it was. Null when the line
    /// has no <c>cols</c> value.
    /// </summary>
    public static string? AdjustColsAttribute(string attrLine, int column, int delta)
    {
        if (ColsValue(attrLine) is not var (start, length)) return null;
        var value = attrLine.Substring(start, length);
        if (value.Trim().Length == 0) return null;

        string updated;
        if (int.TryParse(value.Trim(), out var count))
            updated = Math.Max(1, count + delta).ToString(System.Globalization.CultureInfo.InvariantCulture);
        else
        {
            var specs = new List<string>();
            foreach (var entry in value.Split(','))
            {
                var spec = entry.Trim();
                var star = spec.IndexOf('*');
                if (star > 0 && int.TryParse(spec[..star], out var times))
                    specs.AddRange(Enumerable.Repeat(star + 1 < spec.Length ? spec[(star + 1)..] : "1", times));
                else
                    specs.Add(entry);
            }
            if (delta > 0) specs.Insert(Math.Clamp(column, 0, specs.Count), "1");
            else if (delta < 0 && specs.Count > 1) specs.RemoveAt(Math.Clamp(column, 0, specs.Count - 1));
            updated = string.Join(",", specs);
        }
        return attrLine[..start] + updated + attrLine[(start + length)..];
    }

    /// <summary>An attribute line, such as <c>[%header,cols="1,2"]</c>, that has a <c>cols</c> value.</summary>
    public static bool DeclaresCols(string line) =>
        line.TrimStart().StartsWith('[') && ColsValue(line) is not null;

    /// <summary>Where the <c>cols</c> value is in an attribute line, inside its quotes if quoted.</summary>
    private static (int Start, int Length)? ColsValue(string line)
    {
        for (var at = line.IndexOf("cols=", StringComparison.OrdinalIgnoreCase); at >= 0;
             at = line.IndexOf("cols=", at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (at > 0 && line[at - 1] is not ('[' or ',' or ' ' or '\t')) continue;
            var start = at + 5;
            if (start < line.Length && line[start] is '"' or '\'')
            {
                var close = line.IndexOf(line[start], start + 1);
                return close < 0 ? null : (start + 1, close - start - 1);
            }
            var end = line.IndexOfAny([',', ']'], start);
            return end < 0 ? null : (start, end - start);
        }
        return null;
    }

    /// <summary>A <c>[cols="1,3"]</c> line with the specs of columns <paramref name="a"/> and
    /// <paramref name="b"/> swapped, as their columns are; the line as it was when it has no such specs.</summary>
    public static string SwapColsSpecs(string attrLine, int a, int b)
    {
        var at = attrLine.IndexOf("cols=\"", StringComparison.OrdinalIgnoreCase);
        var end = at < 0 ? -1 : attrLine.IndexOf('"', at + 6);
        if (end < 0) return attrLine;
        var specs = attrLine[(at + 6)..end].Split(',');
        if (a < 0 || b < 0 || a >= specs.Length || b >= specs.Length || specs.Any(s => s.Contains('*'))) return attrLine;
        (specs[a], specs[b]) = (specs[b], specs[a]);
        return attrLine[..(at + 6)] + string.Join(",", specs) + attrLine[end..];
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

    /// <summary>
    /// The cell straight above (<paramref name="step"/> -1) or below (+1) cell <paramref name="from"/>,
    /// by the columns the cells take with their spans: past a cell spanning rows, onto one spanning
    /// columns. <paramref name="spans"/> gives each row's cells as (ColSpan, RowSpan). Null past the
    /// first or last row.
    /// </summary>
    public static (int Row, int Cell)? CellAcross(IReadOnlyList<IReadOnlyList<(int Cols, int Rows)>> spans,
        (int Row, int Cell) from, int step)
    {
        // Which cell covers each column of each row, laid out as a table is.
        var owners = spans.Select(_ => new List<(int Row, int Cell)?>()).ToList();
        for (var r = 0; r < spans.Count; r++)
        {
            var col = 0;
            for (var c = 0; c < spans[r].Count; c++)
            {
                while (col < owners[r].Count && owners[r][col] is not null) col++;
                // ponytail: spans capped at 1000 columns, so a document's 99999+| can't fill memory.
                var (cols, rows) = (Math.Clamp(spans[r][c].Cols, 1, 1000), Math.Max(1, spans[r][c].Rows));
                for (var dr = 0; dr < rows && r + dr < spans.Count; dr++)
                    for (var dc = 0; dc < cols; dc++)
                    {
                        var line = owners[r + dr];
                        while (line.Count <= col + dc) line.Add(null);
                        line[col + dc] = (r, c);
                    }
                col += cols;
            }
        }
        var column = from.Row >= 0 && from.Row < owners.Count ? owners[from.Row].IndexOf(from) : -1;
        if (column < 0) return null; // no such cell
        for (var r = from.Row + step; r >= 0 && r < spans.Count; r += step)
            if (owners[r].Count > 0 && owners[r][Math.Min(column, owners[r].Count - 1)] is { } to && to != from)
                return to; // a shorter row: its last cell
        return null;
    }
}
