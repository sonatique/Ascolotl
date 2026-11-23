using AdocNet;

namespace Ascolotl.Core.Editing;

/// <summary>
/// Pure logic for block-level WYSIWYG editing: resolving a rendered block's
/// AST <see cref="SourceRange"/> to a (start, length) slice of the EDITOR
/// buffer, and deciding whether an edited slice can be safely spliced back.
///
/// <para>Adapted from the upstream AdocNet.Avalonia.Editor sample's
/// SourceRangeOffsets/DecideCommit, with one critical difference: AdocNet's
/// AST coordinates are in <em>post-include-expansion</em> text, while the
/// editor buffer holds the unexpanded source. Every line of the range is
/// therefore translated through the <see cref="Services.IncludeExpansionMap"/>
/// table first; a block whose lines don't all map back contiguously (i.e. it
/// originates in — or straddles — an <c>include::</c>d file) is reported as
/// not editable rather than risking a corrupting splice.</para>
/// </summary>
public static class BlockSourceEditing
{
    /// <summary>
    /// Resolve <paramref name="range"/> (1-based, inclusive end, in
    /// post-include-expansion coordinates) to a 0-based (start, length)
    /// slice of <paramref name="editorText"/>. Returns null when the block
    /// cannot be safely edited: the range is None, any of its lines came
    /// from an included file, the mapped lines aren't contiguous, or the
    /// range falls outside the buffer.
    /// </summary>
    public static (int Start, int Length)? ResolveEditorSlice(
        string editorText, SourceRange range, int[] astToEditor)
    {
        if (string.IsNullOrEmpty(editorText)) return null;
        if (range.IsNone) return null;

        var astStart = range.Start.Line - 1; // 0-based
        var astEnd = range.End.Line - 1;
        if (astStart < 0 || astEnd < astStart) return null;

        var editorStart = Services.IncludeExpansionMap.AstLineToEditor(astToEditor, astStart, fallback: -1);
        var editorEnd = Services.IncludeExpansionMap.AstLineToEditor(astToEditor, astEnd, fallback: -1);
        if (editorStart < 0 || editorEnd < 0) return null;

        // Contiguity: the whole block must map 1:1 onto editor lines. A gap
        // means an include directive sits inside the range — splicing would
        // misalign everything after it.
        if (editorEnd - editorStart != astEnd - astStart) return null;

        var lineStarts = ComputeLineStarts(editorText);
        if (editorStart >= lineStarts.Count || editorEnd >= lineStarts.Count) return null;

        var start = lineStarts[editorStart] + (range.Start.Column - 1);
        var endInclusive = lineStarts[editorEnd] + (range.End.Column - 1);

        // Clamp: column positions were computed against the expanded text;
        // mapped lines are verbatim-identical so they normally agree, but
        // never let a mismatch run past the buffer or the line.
        var endLineEnd = editorEnd + 1 < lineStarts.Count
            ? lineStarts[editorEnd + 1] - 1            // position of the '\n'
            : editorText.Length;
        if (start < 0 || start > editorText.Length) return null;
        if (endInclusive >= endLineEnd) endInclusive = endLineEnd - 1;
        if (endInclusive >= editorText.Length) endInclusive = editorText.Length - 1;
        if (endInclusive < start) return null;

        return (start, endInclusive - start + 1);
    }

    /// <summary>
    /// A table cell's slice, as <see cref="ResolveEditorSlice"/>; but AdocNet marks an empty
    /// cell with the one position after its delimiter, which is the next <c>|</c> or the line's
    /// end: that cell is the empty slice there, to type into.
    /// </summary>
    public static (int Start, int Length)? ResolveCellSlice(string editorText, SourceRange range, int[] astToEditor)
    {
        if (range.IsNone || range.Start != range.End) return ResolveEditorSlice(editorText, range, astToEditor);
        var line = Services.IncludeExpansionMap.AstLineToEditor(astToEditor, range.Start.Line - 1, fallback: -1);
        var lineStarts = ComputeLineStarts(editorText);
        if (line < 0 || line >= lineStarts.Count) return null;
        var lineEnd = line + 1 < lineStarts.Count ? lineStarts[line + 1] - 1 : editorText.Length;
        var at = lineStarts[line] + range.Start.Column - 1;
        if (at < lineStarts[line] || at > lineEnd) return null;
        // ponytail: the default separator only; a table with its own (separator=;) still shows the separator.
        return at == lineEnd || editorText[at] is '|' or '\r' ? (at, 0) : (at, 1);
    }

    /// <summary>
    /// An empty Markdown cell, which AdocNet gives no range, nor a row all empty: the empty slice
    /// inside its pipes, after one space, on row <paramref name="row"/> of the table starting at
    /// <paramref name="table"/> (one line a row, the header's underline after the first).
    /// Null when the row doesn't write that cell out.
    /// </summary>
    public static (int Start, int Length)? EmptyMarkdownCell(string editorText, SourceRange table, int row, int cell, int[] astToEditor)
    {
        if (table.IsNone || row < 0 || cell < 0) return null;
        var astLine = table.Start.Line + (row == 0 ? 0 : row + 1);
        var line = Services.IncludeExpansionMap.AstLineToEditor(astToEditor, astLine - 1, fallback: -1);
        var lineStarts = ComputeLineStarts(editorText);
        if (line < 0 || line >= lineStarts.Count) return null;
        var start = lineStarts[line];
        var end = line + 1 < lineStarts.Count ? lineStarts[line + 1] - 1 : editorText.Length;
        var pipes = new List<int>();
        for (var i = start; i < end; i++)
            if (editorText[i] == '|' && (i == start || editorText[i - 1] != '\\')) pipes.Add(i);
        // With a leading pipe (after a quote's >), cell k lies between pipes k and k + 1; without one, k - 1 and k.
        var open = editorText.AsSpan(start, end - start).TrimStart(" \t>").StartsWith("|") ? cell : cell - 1;
        if (open < 0 || open + 1 >= pipes.Count) return null;
        var at = pipes[open] + 1;
        // Not blank there: not the cell asked for. Better not opened than typed into another.
        if (!editorText.AsSpan(at, pipes[open + 1] - at).IsWhiteSpace()) return null;
        return (at < pipes[open + 1] && editorText[at] == ' ' ? at + 1 : at, 0);
    }

    public enum CommitAction { Replace, NoChange, Abort }

    /// <summary>
    /// Pure decision for committing an in-place edit. Aborts when the range
    /// is out of bounds or the source at the range no longer equals the slice
    /// that was opened for editing (the document shifted underneath — splicing
    /// would corrupt unrelated text). NoChange when the edit is a no-op.
    /// </summary>
    public static (CommitAction Action, int Start, int Length) DecideCommit(
        string sourceText, int start, int length, string originalSlice, string newSlice)
    {
        if (start < 0 || start > sourceText.Length)
            return (CommitAction.Abort, start, length);
        if (start + length > sourceText.Length)
            length = sourceText.Length - start;

        var currentSlice = sourceText.Substring(start, length);
        if (!string.Equals(currentSlice, originalSlice, StringComparison.Ordinal))
            return (CommitAction.Abort, start, length);
        if (string.Equals(newSlice, currentSlice, StringComparison.Ordinal))
            return (CommitAction.NoChange, start, length);
        return (CommitAction.Replace, start, length);
    }

    /// <summary>
    /// Translate an editor offset across a splice that replaced
    /// <c>[spliceStart, spliceStart+removedLen)</c> with
    /// <paramref name="insertedLen"/> characters. An offset at or after the end
    /// of the removed region shifts by the length delta; one before it is
    /// unchanged. (Offsets <em>inside</em> the removed region are ambiguous —
    /// callers use this only for disjoint regions, e.g. a different block — and
    /// are treated as "before".) Used to re-find a queued WYSIWYG re-entry
    /// target after a commit edits an earlier block.
    /// </summary>
    public static int ShiftOffsetForSplice(int offset, int spliceStart, int removedLen, int insertedLen)
        => offset >= spliceStart + removedLen ? offset + (insertedLen - removedLen) : offset;

    /// <summary>Duplicate the slice: insert a copy after it, separated by a
    /// blank line; <paramref name="withoutId"/>: the copy without the block's ID. Returns the new source text.</summary>
    public static string DuplicateSlice(string sourceText, int start, int length, bool withoutId = false)
    {
        var slice = sourceText.Substring(start, length);
        return sourceText.Insert(start + length, "\n\n" + (withoutId ? WithoutBlockId(slice) : slice));
    }

    /// <summary>An AsciiDoc block without its ID in the lines above it: no <c>[[id]]</c> line, no
    /// <c>#id</c> in <c>[source#id]</c>, no <c>id=</c> attribute. A copy must not repeat the ID.</summary>
    // ponytail: an inline anchor in the text itself ("[[id]]Text", "== Title [[id]]") stays.
    public static string WithoutBlockId(string block)
    {
        var lines = block.Split('\n').ToList();
        for (var i = 0; i < lines.Count && IsBlockHeader(lines[i].AsSpan().TrimEnd('\r'));)
        {
            var cr = lines[i].EndsWith('\r') ? "\r" : "";
            if (WithoutId(lines[i].TrimEnd('\r')) is { } kept) lines[i++] = kept + cr;
            else lines.RemoveAt(i);
        }
        return string.Join('\n', lines);
    }

    /// <summary>A header line without the ID it gives; null when nothing else is left of it.</summary>
    private static string? WithoutId(string line)
    {
        if (line.StartsWith("[[", StringComparison.Ordinal)) return line.EndsWith("]]", StringComparison.Ordinal) ? null : line;
        if (line[0] != '[') return line; // .Title
        var entries = SplitAttributes(line[1..^1]);
        var removed = entries.RemoveAll(e => e.TrimStart().StartsWith("id=", StringComparison.Ordinal));
        // The first entry's shorthand, style#id.role%option: the #id goes.
        if (entries.Count > 0 && !entries[0].Contains('=') && entries[0].IndexOf('#') is var hash and >= 0)
        {
            var end = entries[0].IndexOfAny(['.', '%'], hash);
            entries[0] = entries[0].Remove(hash, (end < 0 ? entries[0].Length : end) - hash);
            removed++;
        }
        if (removed == 0) return line;
        return entries.All(string.IsNullOrWhiteSpace) ? null : "[" + string.Join(',', entries) + "]";
    }

    /// <summary>An attribute list split at its commas, outside values in quotes; each entry as written.</summary>
    private static List<string> SplitAttributes(string list)
    {
        var entries = new List<string>();
        var (start, quote) = (0, '\0');
        for (var i = 0; i < list.Length; i++)
        {
            var c = list[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'' && list.AsSpan(start, i - start).IsWhiteSpace()) quote = c;
            else if (c == ',') { entries.Add(list[start..i]); start = i + 1; }
        }
        entries.Add(list[start..]);
        return entries;
    }

    /// <summary>
    /// Delete the slice, absorbing up to two trailing newlines so no hole of
    /// separator-only whitespace is left behind. Returns the new source text.
    /// </summary>
    public static string DeleteSlice(string sourceText, int start, int length)
    {
        var extra = 0;
        if (start + length + extra < sourceText.Length && sourceText[start + length + extra] == '\n') extra++;
        if (start + length + extra < sourceText.Length && sourceText[start + length + extra] == '\n') extra++;
        return sourceText.Remove(start, length + extra);
    }

    /// <summary>
    /// Widen a block's slice to the title, anchor and attribute lines right above it
    /// (<c>.Title</c>, <c>[[id]]</c>, <c>[source,python]</c>, <c>[cols="1,1"]</c>), which AdocNet
    /// leaves out of the block's range: moving or deleting the block takes them along.
    /// Never reaches before <paramref name="notBefore"/>, the end of the block above.
    /// </summary>
    public static (int Start, int Length) WithHeader(string sourceText, int start, int length, int notBefore = 0)
    {
        var lineStart = start == 0 ? 0 : sourceText.LastIndexOf('\n', start - 1) + 1;
        if (!sourceText.AsSpan(lineStart, start - lineStart).IsWhiteSpace()) return (start, length);
        var s = lineStart;
        while (s > notBefore)
        {
            var above = s >= 2 ? sourceText.LastIndexOf('\n', s - 2) + 1 : 0;
            if (above < notBefore || !IsBlockHeader(sourceText.AsSpan(above, s - 1 - above).TrimEnd('\r'))) break;
            s = above;
        }
        return s < lineStart ? (s, start + length - s) : (start, length);
    }

    private static bool IsBlockHeader(ReadOnlySpan<char> line) =>
        line.Length > 1 && (line[0] == '[' && line[^1] == ']' || line[0] == '.' && line[1] is not ('.' or ' ' or '\t'));

    /// <summary>Swap two slices, <paramref name="a"/> before <paramref name="b"/>, keeping what lies
    /// between them; a blank line keeps them two blocks.</summary>
    public static string SwapSlices(string sourceText, (int Start, int Length) a, (int Start, int Length) b)
    {
        var aEnd = a.Start + a.Length;
        var gap = sourceText[aEnd..b.Start];
        if (gap.AsSpan().Count('\n') < 2) gap = "\n\n";
        return sourceText[..a.Start] + sourceText.Substring(b.Start, b.Length) + gap
            + sourceText.Substring(a.Start, a.Length) + sourceText[(b.Start + b.Length)..];
    }

    private static List<int> ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);
        return starts;
    }
}
