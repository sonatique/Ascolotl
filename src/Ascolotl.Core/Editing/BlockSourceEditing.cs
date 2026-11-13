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
    /// blank line. Returns the new source text.</summary>
    public static string DuplicateSlice(string sourceText, int start, int length)
    {
        var slice = sourceText.Substring(start, length);
        return sourceText.Insert(start + length, "\n\n" + slice);
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

    private static List<int> ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);
        return starts;
    }
}
