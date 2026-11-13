namespace Ascolotl.Core.Editing;

/// <summary>One cell of a <see cref="TableGrid"/> — its content plus span.</summary>
public sealed class TableCell
{
    public string Content { get; set; } = string.Empty;
    public int ColSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;

    /// <summary>Non-span spec characters (alignment / style) preserved verbatim
    /// and re-emitted after the span operator, e.g. the <c>^</c> in <c>2+^|</c>.</summary>
    public string SpecExtra { get; set; } = string.Empty;

    public TableCell Clone() => new()
        { Content = Content, ColSpan = ColSpan, RowSpan = RowSpan, SpecExtra = SpecExtra };
}

/// <summary>
/// A span-aware logical model of an AsciiDoc delimited table's body — the cell
/// rows between the <c>|===</c> fences. Parses the source into anchor cells with
/// col/row spans, supports structural edits (merge, split, insert/delete row and
/// column) that keep spans consistent, and serialises back to canonical source.
///
/// <para>Multiplier specs (<c>N*</c>) and multi-line cells aren't modelled —
/// <see cref="Parse"/> returns null for those so the caller can fall back to
/// editing the table source directly.</para>
/// </summary>
public sealed class TableGrid
{
    /// <summary>Total logical columns.</summary>
    public int ColumnCount { get; private set; }

    /// <summary>Explicit (anchor) cells per source row, in source order.</summary>
    public List<List<TableCell>> Rows { get; } = new();

    private TableGrid() { }

    // ---- Parsing ----------------------------------------------------------

    public static TableGrid? Parse(IReadOnlyList<string> bodyLines)
    {
        var grid = new TableGrid();
        foreach (var raw in bodyLines)
        {
            if (raw.Trim().Length == 0) continue; // blank row separator
            var cells = ParseRow(raw);
            if (cells is null) return null;        // unsupported → bail
            grid.Rows.Add(cells);
        }
        if (grid.Rows.Count == 0) return null;

        grid.ColumnCount = grid.Rows[0].Sum(c => c.ColSpan);
        if (grid.ColumnCount <= 0) return null;
        return grid;
    }

    private static List<TableCell>? ParseRow(string line)
    {
        var pipes = UnescapedPipes(line);
        if (pipes.Count == 0) return null;

        // Anything before the first cell's spec must be whitespace.
        if (line[..SpecStart(line, pipes[0])].Trim().Length != 0) return null;

        var cells = new List<TableCell>();
        for (var j = 0; j < pipes.Count; j++)
        {
            var p = pipes[j];
            var specStr = line[SpecStart(line, p)..p];
            if (specStr.Contains('*')) return null; // multiplier — unsupported

            var contentEnd = j + 1 < pipes.Count ? SpecStart(line, pipes[j + 1]) : line.Length;
            var content = line[(p + 1)..contentEnd].Trim();

            if (!TryParseSpec(specStr, out var col, out var row, out var extra))
                return null;

            cells.Add(new TableCell { Content = content, ColSpan = col, RowSpan = row, SpecExtra = extra });
        }
        return cells;
    }

    /// <summary>Start index of the spec run immediately before pipe at
    /// <paramref name="p"/> — back over spec characters to whitespace, line start
    /// or a previous pipe.</summary>
    private static int SpecStart(string line, int p)
    {
        var i = p;
        while (i > 0 && !char.IsWhiteSpace(line[i - 1]) && line[i - 1] != '|' && IsSpecChar(line[i - 1]))
            i--;
        return i;
    }

    private static bool IsSpecChar(char c) =>
        char.IsDigit(c) || c is '.' or '+' or '*' or '<' or '^' or '>'
        || c is 'a' or 'd' or 'e' or 'h' or 'l' or 'm' or 's' or 'v';

    /// <summary>Parse a cell spec into col/row span plus the preserved
    /// alignment/style remainder. Returns false only when the run looks like a
    /// spec but isn't representable; an empty/garbage run yields a 1×1 cell.</summary>
    private static bool TryParseSpec(string spec, out int col, out int row, out string extra)
    {
        col = 1; row = 1; extra = string.Empty;
        if (spec.Length == 0) return true;

        var i = 0;
        var digitsA = ReadDigits(spec, ref i);
        var hasDot = i < spec.Length && spec[i] == '.';
        var digitsB = string.Empty;
        if (hasDot) { i++; digitsB = ReadDigits(spec, ref i); }

        if (i < spec.Length && spec[i] == '+')
        {
            i++;
            col = digitsA.Length > 0 ? int.Parse(digitsA) : 1;
            row = hasDot && digitsB.Length > 0 ? int.Parse(digitsB) : (hasDot ? 1 : 1);
            // ".2+" → col 1, row 2; "2+" → col 2, row 1; "2.3+" → col 2, row 3.
            if (!hasDot) row = 1;
            else if (digitsA.Length == 0) col = 1;
            extra = spec[i..];
            return true;
        }

        // No '+': not a span. Treat the whole run as alignment/style remainder
        // only if it's purely such operators; otherwise it was content.
        foreach (var ch in spec)
            if (char.IsDigit(ch)) return true; // bare digits before '|' → content, 1×1 cell
        extra = spec;
        return true;
    }

    private static string ReadDigits(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return s[start..i];
    }

    private static List<int> UnescapedPipes(string line)
    {
        var positions = new List<int>();
        for (var i = 0; i < line.Length; i++)
            if (line[i] == '|' && (i == 0 || line[i - 1] != '\\'))
                positions.Add(i);
        return positions;
    }

    // ---- Layout (occupancy) ----------------------------------------------

    private (int[][] logicalCol, TableCell?[][] owner, Dictionary<TableCell, int> anchorRow) BuildLayout()
    {
        var rowCount = Rows.Count;
        var owner = new TableCell?[rowCount][];
        for (var r = 0; r < rowCount; r++) owner[r] = new TableCell?[ColumnCount];
        var logicalCol = new int[rowCount][];
        var anchorRow = new Dictionary<TableCell, int>();

        for (var r = 0; r < rowCount; r++)
        {
            logicalCol[r] = new int[Rows[r].Count];
            var c = 0;
            for (var k = 0; k < Rows[r].Count; k++)
            {
                var cell = Rows[r][k];
                while (c < ColumnCount && owner[r][c] is not null) c++;
                logicalCol[r][k] = c;
                anchorRow[cell] = r;
                for (var dr = 0; dr < cell.RowSpan && r + dr < rowCount; dr++)
                    for (var dc = 0; dc < cell.ColSpan && c + dc < ColumnCount; dc++)
                        owner[r + dr][c + dc] = cell;
                c += cell.ColSpan;
            }
        }
        return (logicalCol, owner, anchorRow);
    }

    private static TableCell Empty() => new();

    // ---- Operations -------------------------------------------------------

    /// <summary>Merge the cell with the explicit cell to its right (same row),
    /// growing its column span. Requires matching row spans.</summary>
    public bool MergeRight(int row, int cell)
    {
        if (!Valid(row, cell) || cell + 1 >= Rows[row].Count) return false;
        var a = Rows[row][cell];
        var b = Rows[row][cell + 1];
        if (a.RowSpan != b.RowSpan) return false;
        a.ColSpan += b.ColSpan;
        Rows[row].RemoveAt(cell + 1);
        return true;
    }

    /// <summary>Merge the cell with the one directly below it, growing its row
    /// span. Requires matching column spans and alignment.</summary>
    public bool MergeDown(int row, int cell)
    {
        if (!Valid(row, cell)) return false;
        var (logicalCol, _, _) = BuildLayout();
        var a = Rows[row][cell];
        var lc = logicalCol[row][cell];
        var belowRow = row + a.RowSpan;
        if (belowRow >= Rows.Count) return false;

        var bk = -1;
        for (var k = 0; k < Rows[belowRow].Count; k++)
            if (logicalCol[belowRow][k] == lc) { bk = k; break; }
        if (bk < 0) return false;

        var b = Rows[belowRow][bk];
        if (b.ColSpan != a.ColSpan) return false;
        a.RowSpan += b.RowSpan;
        Rows[belowRow].RemoveAt(bk);
        return true;
    }

    /// <summary>Reset a spanned cell back to 1×1, filling the freed grid
    /// positions with empty cells.</summary>
    public bool Split(int row, int cell)
    {
        if (!Valid(row, cell)) return false;
        var (logicalCol, _, _) = BuildLayout();
        var c0 = Rows[row][cell];
        if (c0.ColSpan == 1 && c0.RowSpan == 1) return false;

        var lc = logicalCol[row][cell];
        int cspan = c0.ColSpan, rspan = c0.RowSpan;
        c0.ColSpan = 1;
        c0.RowSpan = 1;

        for (var r = row; r < row + rspan && r < Rows.Count; r++)
        {
            for (var c = lc; c < lc + cspan; c++)
            {
                if (r == row && c == lc) continue; // the anchor keeps its spot
                InsertEmptyAtLogicalCol(r, c);
            }
        }
        return true;
    }

    private void InsertEmptyAtLogicalCol(int row, int targetCol)
    {
        var (logicalCol, _, _) = BuildLayout();
        var idx = Rows[row].Count;
        for (var k = 0; k < Rows[row].Count; k++)
            if (logicalCol[row][k] >= targetCol) { idx = k; break; }
        Rows[row].Insert(idx, Empty());
    }

    /// <summary>Insert a blank row above or below the given row, extending any
    /// row-spanning cell that crosses the new boundary instead of placing a cell
    /// in its columns.</summary>
    public bool InsertRow(int row, bool below)
    {
        if (row < 0 || row >= Rows.Count) return false;
        var (_, owner, anchorRow) = BuildLayout();
        var ri = below ? row + 1 : row;

        var newRow = new List<TableCell>();
        var bumped = new HashSet<TableCell>();
        for (var c = 0; c < ColumnCount; c++)
        {
            var up = ri > 0 && ri < Rows.Count ? owner[ri - 1][c] : null;
            var crossing = up is not null && ReferenceEquals(owner[ri][c], up);
            if (crossing)
            {
                if (anchorRow[up!] < ri && bumped.Add(up!)) up!.RowSpan += 1;
            }
            else
            {
                newRow.Add(Empty());
            }
        }
        Rows.Insert(ri, newRow);
        return true;
    }

    /// <summary>Delete a row: shrink row-spans that cross into it, re-anchor
    /// cells that start in it and span downward, and drop the rest.</summary>
    public bool DeleteRow(int row)
    {
        if (row < 0 || row >= Rows.Count || Rows.Count <= 1) return false;
        var (logicalCol, owner, anchorRow) = BuildLayout();

        var shrunk = new HashSet<TableCell>();
        for (var c = 0; c < ColumnCount; c++)
        {
            if (owner[row][c] is { } cell && anchorRow[cell] < row && shrunk.Add(cell))
                cell.RowSpan = Math.Max(1, cell.RowSpan - 1);
        }

        var movers = new List<(TableCell cell, int lc)>();
        for (var k = 0; k < Rows[row].Count; k++)
        {
            var cell = Rows[row][k];
            if (cell.RowSpan > 1)
            {
                cell.RowSpan -= 1;
                movers.Add((cell, logicalCol[row][k]));
            }
        }

        Rows.RemoveAt(row);

        // The row that was below is now at `row`; re-home the downward-spanning
        // cells into it at their logical columns.
        if (row < Rows.Count)
        {
            foreach (var (cell, lc) in movers.OrderBy(m => m.lc))
            {
                var (lc2, _, _) = BuildLayout();
                var idx = Rows[row].Count;
                for (var k = 0; k < Rows[row].Count; k++)
                    if (lc2[row][k] >= lc) { idx = k; break; }
                Rows[row].Insert(idx, cell);
            }
        }
        return true;
    }

    /// <summary>Insert an empty column left/right of the addressed cell. Only
    /// supported when the table has no row spans (returns false otherwise).</summary>
    public bool InsertColumn(int row, int cell, bool right)
    {
        if (!Valid(row, cell) || HasRowSpans()) return false;
        var at = LogicalColNoRowSpan(row, cell) + (right ? Rows[row][cell].ColSpan : 0);
        foreach (var r in Rows) InsertColInRow(r, at);
        ColumnCount += 1;
        return true;
    }

    /// <summary>Delete the addressed cell's column. Only supported when the table
    /// has no row spans (returns false otherwise).</summary>
    public bool DeleteColumn(int row, int cell)
    {
        if (!Valid(row, cell) || HasRowSpans() || ColumnCount <= 1) return false;
        var at = LogicalColNoRowSpan(row, cell);
        foreach (var r in Rows) DeleteColInRow(r, at);
        ColumnCount -= 1;
        return true;
    }

    private bool HasRowSpans() => Rows.Any(r => r.Any(c => c.RowSpan > 1));

    private int LogicalColNoRowSpan(int row, int cell)
    {
        var c = 0;
        for (var k = 0; k < cell; k++) c += Rows[row][k].ColSpan;
        return c;
    }

    private static void InsertColInRow(List<TableCell> row, int at)
    {
        var c = 0;
        for (var k = 0; k < row.Count; k++)
        {
            if (c == at) { row.Insert(k, Empty()); return; }
            if (c < at && at < c + row[k].ColSpan) { row[k].ColSpan += 1; return; }
            c += row[k].ColSpan;
        }
        row.Add(Empty());
    }

    private static void DeleteColInRow(List<TableCell> row, int at)
    {
        var c = 0;
        for (var k = 0; k < row.Count; k++)
        {
            if (at >= c && at < c + row[k].ColSpan)
            {
                if (row[k].ColSpan > 1) row[k].ColSpan -= 1;
                else row.RemoveAt(k);
                return;
            }
            c += row[k].ColSpan;
        }
    }

    private bool Valid(int row, int cell) =>
        row >= 0 && row < Rows.Count && cell >= 0 && cell < Rows[row].Count;

    // ---- Serialisation ----------------------------------------------------

    public List<string> ToLines()
    {
        var lines = new List<string>();
        foreach (var row in Rows)
            lines.Add(string.Join(" ", row.Select(SerializeCell)).TrimEnd());
        return lines;
    }

    private static string SerializeCell(TableCell c)
    {
        var span = string.Empty;
        if (c.ColSpan > 1 && c.RowSpan > 1) span = $"{c.ColSpan}.{c.RowSpan}+";
        else if (c.ColSpan > 1) span = $"{c.ColSpan}+";
        else if (c.RowSpan > 1) span = $".{c.RowSpan}+";

        var prefix = span + c.SpecExtra;
        return c.Content.Length == 0 ? (prefix + "|").TrimEnd() : $"{prefix}| {c.Content}";
    }
}
