namespace Ascolotl.Tests.Unit.Editing;

using AdocNet;
using Ascolotl.Core.Editing;

/// <summary>
/// Tests for <see cref="BlockSourceEditing"/> — the pure logic behind
/// block-level WYSIWYG editing: AST-range → editor-slice resolution
/// (include-aware) and the commit decision with its stale-offset guard.
/// </summary>
public class BlockSourceEditingTests
{
    private static SourceRange Range(int startLine, int startCol, int endLine, int endCol)
        => new(new SourcePosition(startLine, startCol), new SourcePosition(endLine, endCol));

    /// <summary>Identity map for a doc with no includes: ast line i == editor line i.</summary>
    private static int[] IdentityMap(int lines)
    {
        var map = new int[lines];
        for (var i = 0; i < lines; i++) map[i] = i;
        return map;
    }

    // ── ResolveEditorSlice ────────────────────────────────────────────────

    [Fact]
    public void Resolve_SingleLineBlock_IdentityMap()
    {
        var text = "= Title\n\nHello world.\n";
        var slice = BlockSourceEditing.ResolveEditorSlice(text, Range(3, 1, 3, 12), IdentityMap(4));

        Assert.NotNull(slice);
        Assert.Equal("Hello world.", text.Substring(slice.Value.Start, slice.Value.Length));
    }

    [Fact]
    public void Resolve_MultiLineBlock_IdentityMap()
    {
        var text = "First.\nSecond line\nthird continues.\n";
        // Lines 2-3 inclusive, ending at col 16 ('.')
        var slice = BlockSourceEditing.ResolveEditorSlice(text, Range(2, 1, 3, 16), IdentityMap(3));

        Assert.NotNull(slice);
        Assert.Equal("Second line\nthird continues.", text.Substring(slice.Value.Start, slice.Value.Length));
    }

    [Fact]
    public void Resolve_BlockAfterInclude_ShiftedMap()
    {
        // Editor: line0 "= T", line1 "include::x[]", line2 "", line3 "Para."
        // AST: include expands to 3 lines, so ast lines: 0 "= T", 1..3 included, 4 "", 5 "Para."
        var editor = "= T\ninclude::x[]\n\nPara.\n";
        var map = new[] { 0, -1, -1, -1, 2, 3 };

        var slice = BlockSourceEditing.ResolveEditorSlice(editor, Range(6, 1, 6, 5), map);

        Assert.NotNull(slice);
        Assert.Equal("Para.", editor.Substring(slice.Value.Start, slice.Value.Length));
    }

    [Fact]
    public void Resolve_BlockInsideInclude_NotEditable()
    {
        var editor = "= T\ninclude::x[]\n\nPara.\n";
        var map = new[] { 0, -1, -1, -1, 2, 3 };

        // AST lines 2-3 are inside the included file.
        var slice = BlockSourceEditing.ResolveEditorSlice(editor, Range(2, 1, 3, 4), map);

        Assert.Null(slice);
    }

    [Fact]
    public void Resolve_BlockStraddlingInclude_NotEditable()
    {
        // Hypothetical block spanning a real line and included content:
        // mapped lines exist but are non-contiguous.
        var editor = "a\ninclude::x[]\nb\n";
        var map = new[] { 0, -1, -1, 2 };

        var slice = BlockSourceEditing.ResolveEditorSlice(editor, Range(1, 1, 4, 1), map);

        Assert.Null(slice);
    }

    [Fact]
    public void Resolve_NoneRange_NotEditable()
    {
        Assert.Null(BlockSourceEditing.ResolveEditorSlice("text\n", SourceRange.None, IdentityMap(1)));
    }

    [Fact]
    public void Resolve_OverlongEndColumn_ClampsToLineEnd()
    {
        var text = "abc\ndef\n";
        // End column 99 on line 2 — clamp to the line's last char.
        var slice = BlockSourceEditing.ResolveEditorSlice(text, Range(1, 1, 2, 99), IdentityMap(2));

        Assert.NotNull(slice);
        Assert.Equal("abc\ndef", text.Substring(slice.Value.Start, slice.Value.Length));
    }

    [Fact]
    public void Resolve_RangeBeyondBuffer_NotEditable()
    {
        var text = "one\n";
        Assert.Null(BlockSourceEditing.ResolveEditorSlice(text, Range(5, 1, 6, 3), IdentityMap(1)));
    }

    // ── DecideCommit ──────────────────────────────────────────────────────

    [Fact]
    public void Commit_EditedSlice_Replaces()
    {
        var text = "aaa\nbbb\nccc\n";
        var (action, s, len) = BlockSourceEditing.DecideCommit(text, 4, 3, "bbb", "BBB!");

        Assert.Equal(BlockSourceEditing.CommitAction.Replace, action);
        Assert.Equal("aaa\nBBB!\nccc\n", text.Remove(s, len).Insert(s, "BBB!"));
    }

    [Fact]
    public void Commit_UnchangedSlice_NoChange()
    {
        var (action, _, _) = BlockSourceEditing.DecideCommit("aaa\nbbb\n", 4, 3, "bbb", "bbb");
        Assert.Equal(BlockSourceEditing.CommitAction.NoChange, action);
    }

    [Fact]
    public void Commit_DocumentShiftedUnderneath_Aborts()
    {
        // The source at the range no longer matches what was opened.
        var (action, _, _) = BlockSourceEditing.DecideCommit("aaa\nXXX\n", 4, 3, "bbb", "edited");
        Assert.Equal(BlockSourceEditing.CommitAction.Abort, action);
    }

    [Fact]
    public void Commit_RangePastEnd_Aborts()
    {
        var (action, _, _) = BlockSourceEditing.DecideCommit("ab", 10, 3, "xyz", "edited");
        Assert.Equal(BlockSourceEditing.CommitAction.Abort, action);
    }

    [Fact]
    public void Commit_LengthOverrunsEnd_ClampsThenCompares()
    {
        // start valid, length overruns: clamp, then the comparison decides.
        var (action, s, len) = BlockSourceEditing.DecideCommit("abcde", 3, 99, "de", "DE");
        Assert.Equal(BlockSourceEditing.CommitAction.Replace, action);
        Assert.Equal(3, s);
        Assert.Equal(2, len);
    }

    // ── Duplicate / Delete ────────────────────────────────────────────────

    [Fact]
    public void Duplicate_InsertsCopyAfterBlankLine()
    {
        var text = "one\n\ntwo\n\nthree\n";
        // Duplicate "two" (offset 5, length 3)
        var result = BlockSourceEditing.DuplicateSlice(text, 5, 3);
        Assert.Equal("one\n\ntwo\n\ntwo\n\nthree\n", result);
    }

    [Fact]
    public void Delete_AbsorbsTrailingBlankLine()
    {
        var text = "one\n\ntwo\n\nthree\n";
        // Delete "two" (offset 5, length 3) + its "\n\n" separator
        var result = BlockSourceEditing.DeleteSlice(text, 5, 3);
        Assert.Equal("one\n\nthree\n", result);
    }

    [Fact]
    public void Delete_AtEndOfText_NoOverrun()
    {
        var text = "one\n\ntwo";
        var result = BlockSourceEditing.DeleteSlice(text, 5, 3);
        Assert.Equal("one\n\n", result);
    }

    // ── ShiftOffsetForSplice ──────────────────────────────────────────────

    [Fact]
    public void Shift_OffsetBeforeSplice_Unchanged()
    {
        // Splice at [10,15) replaced by 20 chars; an offset at 5 is before it.
        Assert.Equal(5, BlockSourceEditing.ShiftOffsetForSplice(5, 10, 5, 20));
    }

    [Fact]
    public void Shift_OffsetAfterSplice_Grows()
    {
        // [10,15) (len 5) → 8 chars: delta +3; offset 30 → 33.
        Assert.Equal(33, BlockSourceEditing.ShiftOffsetForSplice(30, 10, 5, 8));
    }

    [Fact]
    public void Shift_OffsetAfterSplice_Shrinks()
    {
        // [10,18) (len 8) → 2 chars: delta -6; offset 30 → 24.
        Assert.Equal(24, BlockSourceEditing.ShiftOffsetForSplice(30, 10, 8, 2));
    }

    [Fact]
    public void Shift_OffsetAtEndOfRemoved_CountsAsAfter()
    {
        // Boundary: offset == spliceStart + removedLen (15) shifts by the delta.
        Assert.Equal(18, BlockSourceEditing.ShiftOffsetForSplice(15, 10, 5, 8));
    }

    [Fact]
    public void Shift_PureInsert_ShiftsOnlyAtOrAfter()
    {
        // removedLen 0 at offset 10, inserting 4: offset 10 → 14, offset 9 → 9.
        Assert.Equal(14, BlockSourceEditing.ShiftOffsetForSplice(10, 10, 0, 4));
        Assert.Equal(9, BlockSourceEditing.ShiftOffsetForSplice(9, 10, 0, 4));
    }

    // ── ResolveCellSlice ──────────────────────────────────────────────────

    [Theory]
    [InlineData("| x ||\n", 3, "x")]     // one character
    [InlineData("| x ||\n", 6, "")]      // empty, before the next delimiter: not the delimiter
    [InlineData("| |\n", 4, "")]         // empty, at the line's end
    [InlineData("| |", 4, "")]           // at the text's end
    [InlineData("| |\r\n", 4, "")]
    public void ResolveCell_EmptyCellIsAnEmptySliceToTypeInto(string text, int column, string expected)
    {
        var slice = BlockSourceEditing.ResolveCellSlice(text, Range(1, column, 1, column), IdentityMap(2));
        Assert.NotNull(slice);
        Assert.Equal(expected, text.Substring(slice.Value.Start, slice.Value.Length));
        Assert.Equal(column - 1, slice.Value.Start);
    }

    // ── WithHeader / SwapSlices ───────────────────────────────────────────

    private static string Widened(string text, string block, int notBefore = 0)
    {
        var (s, len) = BlockSourceEditing.WithHeader(text, text.IndexOf(block, StringComparison.Ordinal), block.Length, notBefore);
        return text.Substring(s, len);
    }

    [Fact]
    public void WithHeader_TakesTheTitleAnchorAndAttributeLines()
    {
        var text = "Para.\n\n[[code]]\n.Example\n[source,python]\n----\nx\n----\n";
        Assert.Equal("[[code]]\n.Example\n[source,python]\n----\nx\n----", Widened(text, "----\nx\n----"));
        Assert.Equal("[cols=\"1,1\"]\r\n|===\r\n|===", Widened("A.\r\n\r\n[cols=\"1,1\"]\r\n|===\r\n|===", "|===\r\n|==="));
    }

    [Theory]
    [InlineData("Para.\n\n----\nx\n----")]            // a blank line above
    [InlineData(". item\n----\nx\n----")]             // a list item, not a title
    [InlineData("....\n----\nx\n----")]               // a literal block's fence
    [InlineData("----\nx\n----")]                     // the first line
    public void WithHeader_LeavesOtherLinesAbove(string text)
    {
        Assert.Equal("----\nx\n----", Widened(text, "----\nx\n----"));
    }

    [Fact]
    public void WithHeader_StopsAtTheBlockAbove()
    {
        var text = "[a]\n[b]\n----\nx\n----";
        Assert.Equal("[b]\n----\nx\n----", Widened(text, "----\nx\n----", notBefore: 3));
    }

    [Fact]
    public void SwapSlices_SwapsTwoBlocks_AndKeepsWhatIsBetween()
    {
        var text = "One.\n\n// note\n\nTwo.\n";
        Assert.Equal("Two.\n\n// note\n\nOne.\n", BlockSourceEditing.SwapSlices(text, (0, 4), (text.IndexOf("Two.", StringComparison.Ordinal), 4)));
        Assert.Equal("Para.\n\n== H\n", BlockSourceEditing.SwapSlices("== H\nPara.\n", (0, 4), (5, 5))); // two blocks still
    }
}
