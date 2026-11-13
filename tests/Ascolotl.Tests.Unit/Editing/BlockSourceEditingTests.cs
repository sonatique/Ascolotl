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
}
