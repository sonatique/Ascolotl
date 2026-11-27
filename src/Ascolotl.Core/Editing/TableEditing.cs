namespace Ascolotl.Core.Editing;

/// <summary>A structural edit of the table at the caret.</summary>
public enum TableOp
{
    RowAbove,
    RowBelow,
    ColumnLeft,
    ColumnRight,
    DeleteRow,
    DeleteColumn,
    MergeRight,
    MergeDown,
    SplitCell,
    MoveRowUp,
    MoveRowDown,
    MoveColumnLeft,
    MoveColumnRight,
    SortAscending,
    SortDescending,
}

/// <summary>Replace <see cref="Length"/> characters at <see cref="Start"/> with
/// <see cref="Text"/>, then put the caret at <see cref="Caret"/> (in the new text).</summary>
public readonly record struct TableEdit(int Start, int Length, string Text, int Caret);

/// <summary>
/// Row and column edits of the table around a caret in source text: AsciiDoc
/// <c>|===</c> tables through <see cref="TableGrid"/> (spans included), and
/// Markdown pipe tables, which have no merged cells. The caret ends up in the
/// cell the edit made or kept.
/// </summary>
public static class TableEditing
{
    /// <summary>Markdown tables can't merge cells.</summary>
    public static bool Supports(TableOp op, bool markdown)
        => !markdown || op is not (TableOp.MergeRight or TableOp.MergeDown or TableOp.SplitCell);

    /// <summary>How sorted cells follow each other: numbers by value and before text, text as the
    /// reader's language sorts it, case aside.</summary>
    public static readonly IComparer<string> SortOrder = Comparer<string>.Create((a, b) =>
    {
        static bool Number(string s, out double value) =>
            double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out value)
            || double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        var (isA, isB) = (Number(a, out var x), Number(b, out var y));
        return isA && isB ? x.CompareTo(y) : isA ? -1 : isB ? 1
            : string.Compare(a.Trim(), b.Trim(), System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CompareOptions.IgnoreCase);
    });

    /// <summary>The caret is on a row of a table.</summary>
    public static bool IsInTable(string text, int caret, bool markdown)
    {
        var lines = new Lines(text);
        var line = lines.IndexOf(caret);
        return markdown ? MarkdownTable(lines, line) is not null : AsciiDocTable(lines, line) is not null;
    }

    /// <summary>The edit, or null when the caret isn't on a table row, the table's
    /// layout can't be edited this way, or the edit isn't possible (e.g. deleting
    /// the only column).</summary>
    public static TableEdit? Apply(TableOp op, string text, int caret, bool markdown)
    {
        if (!Supports(op, markdown)) return null;
        var lines = new Lines(text);
        var line = lines.IndexOf(caret);
        var column = caret - lines.Start(line);
        return markdown ? ApplyMarkdown(op, lines, line, column) : ApplyAsciiDoc(op, lines, line, column);
    }

    // ── AsciiDoc ──────────────────────────────────────────────────────────

    /// <summary>The fences around a body line: an odd number of fences above means it's inside.</summary>
    private static (int Open, int Close)? AsciiDocTable(Lines lines, int line)
    {
        if (lines[line].Trim().Length == 0) return null;
        int open = -1, fences = 0;
        for (var i = 0; i < line; i++)
            if (Assist.AsciiDocContextAnalyzer.IsTableFence(lines[i].Trim())) { fences++; open = i; }
        if (fences % 2 == 0 || Assist.AsciiDocContextAnalyzer.IsTableFence(lines[line].Trim())) return null;
        for (var i = line + 1; i < lines.Count; i++)
            if (Assist.AsciiDocContextAnalyzer.IsTableFence(lines[i].Trim())) return (open, i);
        return null;
    }

    private static TableEdit? ApplyAsciiDoc(TableOp op, Lines lines, int line, int column)
    {
        if (AsciiDocTable(lines, line) is not var (open, close)) return null;
        var body = Enumerable.Range(open + 1, close - open - 1).Select(i => lines[i]).ToList();

        // A [cols=…] line just above the fence sets the width and follows column edits.
        int? colsLine = null;
        for (var i = open - 1; i >= Math.Max(0, open - 3) && lines[i].Trim().Length > 0; i--)
            if (TableStructure.DeclaresCols(lines[i])) { colsLine = i; break; }

        var grid = TableGrid.Parse(body, colsLine is int cl ? ColsCount(lines[cl]) : null);
        if (grid is null) return null;

        var row = body.Take(line - open - 1).Count(l => l.Trim().Length > 0);
        var pipes = Pipes(lines[line]);
        var cell = Math.Clamp(pipes.Count(p => p < column) - 1, 0, grid.Rows[row].Count - 1);
        // options="header" or %header in the attribute lines above: row 0 stays first.
        var header = Enumerable.Range(Math.Max(0, open - 3), open - Math.Max(0, open - 3))
            .Any(i => lines[i].TrimStart().StartsWith('[') && lines[i].Contains("header", StringComparison.Ordinal));

        var (done, colDelta, toRow, toCell) = op switch
        {
            TableOp.MoveRowUp => (grid.MoveRow(row, -1, header), 0, row - 1, cell),
            TableOp.MoveRowDown => (grid.MoveRow(row, +1, header), 0, row + 1, cell),
            TableOp.MoveColumnLeft => (grid.MoveColumn(row, cell, -1), 0, row, cell - 1),
            TableOp.MoveColumnRight => (grid.MoveColumn(row, cell, +1), 0, row, cell + 1),
            TableOp.SortAscending => (grid.SortRows(row, cell, descending: false, header), 0, row, cell),
            TableOp.SortDescending => (grid.SortRows(row, cell, descending: true, header), 0, row, cell),
            TableOp.RowAbove => (grid.InsertRow(row, below: false, header), 0, row, 0),
            TableOp.RowBelow => (grid.InsertRow(row, below: true), 0, row + 1, 0),
            TableOp.DeleteRow => (grid.DeleteRow(row), 0, Math.Max(0, row - 1), 0),
            TableOp.ColumnLeft => (grid.InsertColumn(row, cell, right: false), 1, row, cell),
            TableOp.ColumnRight => (grid.InsertColumn(row, cell, right: true), 1, row, cell + 1),
            TableOp.DeleteColumn => (grid.DeleteColumn(row, cell), -1, row, Math.Max(0, cell - 1)),
            TableOp.MergeRight => (grid.MergeRight(row, cell), 0, row, cell),
            TableOp.MergeDown => (grid.MergeDown(row, cell), 0, row, cell),
            _ => (grid.Split(row, cell), 0, row, cell),
        };
        if (!done) return null;

        var first = colsLine ?? open;
        var newLines = new List<string>();
        for (var i = first; i <= open; i++)
            newLines.Add(i != colsLine ? lines[i]
                : colDelta != 0 ? TableStructure.AdjustColsAttribute(lines[i], grid.EditedColumn, colDelta) ?? lines[i]
                : op is TableOp.MoveColumnLeft or TableOp.MoveColumnRight ? TableStructure.SwapColsSpecs(lines[i], cell, toCell)
                : lines[i]);
        var bodyAt = newLines.Count;
        newLines.AddRange(grid.ToLines());
        newLines.Add(lines[close]);

        // The caret goes to the target row's cell, in the rows written back.
        var target = bodyAt;
        for (var seen = -1; target < newLines.Count - 1; target++)
            if (newLines[target].Trim().Length > 0 && ++seen == Math.Min(toRow, grid.Rows.Count - 1)) break;
        return Replace(lines, first, close, newLines, target, CellContent(newLines[target], toCell));
    }

    /// <summary>Columns a <c>[cols=…]</c> line declares (<c>3</c>, <c>"1,2,1"</c>, <c>"3*"</c>).</summary>
    private static int? ColsCount(string line)
    {
        var at = line.IndexOf("cols=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var value = line[(at + 5)..];
        // Quoted: up to the closing quote; bare: up to the next , or ].
        value = value.StartsWith('"') ? value[1..Math.Max(1, value.IndexOf('"', 1))]
            : value[..Math.Max(0, value.IndexOfAny([',', ']']))];
        var spec = value.Split(',');
        if (spec.Length == 1 && int.TryParse(spec[0].Trim(), out var n)) return n;
        var count = 0;
        foreach (var s in spec)
        {
            var star = s.IndexOf('*');
            count += star > 0 && int.TryParse(s[..star].Trim(), out var times) ? times : 1;
        }
        return count > 0 ? count : null;
    }

    // ── Markdown ──────────────────────────────────────────────────────────

    /// <summary>The run of pipe lines around the caret, when its second line is a delimiter row.</summary>
    private static (int First, int Last)? MarkdownTable(Lines lines, int line)
    {
        static bool Row(string l) => l.Trim().Length > 0 && l.Contains('|');
        if (!Row(lines[line])) return null;
        var (first, last) = (line, line);
        while (first > 0 && Row(lines[first - 1])) first--;
        while (last + 1 < lines.Count && Row(lines[last + 1])) last++;
        if (last == first || !Delimiter(lines[first + 1]) || line == first + 1) return null;
        return (first, last);
    }

    private static bool Delimiter(string line)
        => Cells(line) is { Count: > 0 } cells && cells.All(c => c.Length > 0 && c.Trim(':').Length > 0 && c.Trim(':').All(ch => ch == '-'));

    private static TableEdit? ApplyMarkdown(TableOp op, Lines lines, int line, int column)
    {
        if (MarkdownTable(lines, line) is not var (first, last)) return null;
        var rows = Enumerable.Range(first, last - first + 1).Select(i => Cells(lines[i])).ToList();
        var align = rows[1];
        rows.RemoveAt(1);
        // Ragged rows are padded, never cut: a cell past the header's width is kept.
        var width = rows.Max(r => r.Count);
        foreach (var r in rows.Append(align))
            while (r.Count < width) r.Add(r == align ? "---" : string.Empty);

        var row = line == first ? 0 : line - first - 1;   // 0 is the header
        var leading = lines[line].TrimStart().StartsWith('|') ? 1 : 0;
        var cell = Math.Clamp(Pipes(lines[line]).Count(p => p < column) - leading, 0, width - 1);

        int toRow = row, toCell = cell;
        switch (op)
        {
            case TableOp.RowAbove when row > 0:
                rows.Insert(row, Blank(width));
                break;
            case TableOp.RowBelow:
                rows.Insert(row + 1, Blank(width));
                toRow = row + 1;
                toCell = 0;
                break;
            case TableOp.DeleteRow when row > 0:
                rows.RemoveAt(row);
                toRow = Math.Min(row, rows.Count - 1);
                break;
            case TableOp.ColumnLeft or TableOp.ColumnRight:
                var at = op == TableOp.ColumnRight ? cell + 1 : cell;
                foreach (var r in rows) r.Insert(at, string.Empty);
                align.Insert(at, "---");
                toCell = at;
                break;
            case TableOp.DeleteColumn when width > 1:
                foreach (var r in rows) r.RemoveAt(cell);
                align.RemoveAt(cell);
                toCell = Math.Max(0, cell - 1);
                break;
            // The header row (0) stays first: only body rows move or sort.
            case TableOp.MoveRowUp or TableOp.MoveRowDown when row > 0 && row + (op == TableOp.MoveRowUp ? -1 : 1) is var other && other > 0 && other < rows.Count:
                (rows[row], rows[other]) = (rows[other], rows[row]);
                toRow = other;
                break;
            case TableOp.MoveColumnLeft or TableOp.MoveColumnRight when cell + (op == TableOp.MoveColumnLeft ? -1 : 1) is var to && to >= 0 && to < width:
                foreach (var r in rows.Append(align)) (r[cell], r[to]) = (r[to], r[cell]);
                toCell = to;
                break;
            case TableOp.SortAscending or TableOp.SortDescending when rows.Count > 2:
                var sorted = rows.Skip(1).OrderBy(r => r[cell], TableEditing.SortOrder).ToList();
                if (op == TableOp.SortDescending) sorted = rows.Skip(1).OrderByDescending(r => r[cell], TableEditing.SortOrder).ToList();
                rows.RemoveRange(1, sorted.Count);
                rows.AddRange(sorted);
                break;
            default:
                return null;
        }

        // Written back aligned: every column as wide as its widest cell, indented as the header was
        // (in a list item, say).
        var count = rows[0].Count;
        var widths = Enumerable.Range(0, count).Select(c => Math.Max(3, rows.Max(r => r[c].Length))).ToArray();
        var indent = lines[first][..^lines[first].TrimStart().Length];
        string Write(IEnumerable<string> cells) => indent + "| " + string.Join(" | ", cells) + " |";
        var newLines = rows.Select(r => Write(r.Select((t, c) => t.PadRight(widths[c])))).ToList();
        newLines.Insert(1, Write(align.Take(count).Select((a, c) => Dashes(a, widths[c]))));

        var target = toRow == 0 ? 0 : toRow + 1;
        var offset = indent.Length + 2 + widths.Take(toCell).Sum(w => w + 3);
        return Replace(lines, first, last, newLines, target, offset);
    }

    private static List<string> Blank(int width) => [.. Enumerable.Repeat(string.Empty, width)];

    /// <summary>A delimiter cell at <paramref name="width"/>, keeping its alignment colons.</summary>
    private static string Dashes(string spec, int width)
    {
        var (left, right) = (spec.StartsWith(':'), spec.EndsWith(':') && spec.Length > 1);
        var dashes = new string('-', Math.Max(1, width - (left ? 1 : 0) - (right ? 1 : 0)));
        return (left ? ":" : "") + dashes + (right ? ":" : "");
    }

    /// <summary>A Markdown row's cells, without its outer pipes.</summary>
    private static List<string> Cells(string line)
    {
        var t = line.Trim();
        var pipes = Pipes(t);
        var from = pipes.Count > 0 && pipes[0] == 0 ? 1 : 0;
        var to = pipes.Count > 0 && pipes[^1] == t.Length - 1 && t.Length > 1 ? t.Length - 1 : t.Length;
        var inner = t[from..Math.Max(from, to)];
        var cells = new List<string>();
        var start = 0;
        foreach (var p in Pipes(inner))
        {
            cells.Add(inner[start..p].Trim());
            start = p + 1;
        }
        cells.Add(inner[start..].Trim());
        return cells;
    }

    // ── Shared ────────────────────────────────────────────────────────────

    private static List<int> Pipes(string line)
    {
        var pipes = new List<int>();
        for (var i = 0; i < line.Length; i++)
            if (line[i] == '|' && (i == 0 || line[i - 1] != '\\')) pipes.Add(i);
        return pipes;
    }

    /// <summary>Where cell <paramref name="cell"/>'s text starts in an AsciiDoc row line.</summary>
    private static int CellContent(string line, int cell)
    {
        var pipes = Pipes(line);
        if (pipes.Count == 0) return 0;
        var p = pipes[Math.Clamp(cell, 0, pipes.Count - 1)] + 1;
        return p < line.Length && line[p] == ' ' ? p + 1 : p;
    }

    private static TableEdit Replace(Lines lines, int first, int last, List<string> newLines, int caretLine, int caretColumn)
    {
        var start = lines.Start(first);
        var end = lines.End(last);
        var text = string.Join(lines.Newline, newLines);
        var caret = start + newLines.Take(caretLine).Sum(l => l.Length + lines.Newline.Length)
            + Math.Min(caretColumn, newLines[caretLine].Length);
        return new TableEdit(start, end - start, text, caret);
    }

    /// <summary>The text's lines, without their line ends, and where each starts.</summary>
    private sealed class Lines
    {
        private readonly string _text;
        private readonly List<int> _starts = [0];

        public Lines(string text)
        {
            _text = text;
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') _starts.Add(i + 1);
            Newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        }

        public string Newline { get; }

        public int Count => _starts.Count;

        public int Start(int line) => _starts[line];

        /// <summary>Where the line's text ends, before its line end.</summary>
        public int End(int line)
        {
            var end = line + 1 < _starts.Count ? _starts[line + 1] - 1 : _text.Length;
            return end > _starts[line] && _text[end - 1] == '\r' && line + 1 < _starts.Count ? end - 1 : end;
        }

        public string this[int line] => _text[Start(line)..End(line)];

        public int IndexOf(int offset)
        {
            var i = _starts.BinarySearch(Math.Clamp(offset, 0, _text.Length));
            return i >= 0 ? i : ~i - 1;
        }
    }
}
