namespace Ascolotl.Core.Models;

public enum EditorMode
{
    Source,
    Split,
    /// <summary>
    /// Preview-only layout with block-level WYSIWYG editing: double-click a
    /// rendered block to edit its AsciiDoc source in place. The source pane
    /// is hidden, but the source text remains the single source of truth —
    /// every in-place commit splices back into it.
    /// </summary>
    Wysiwyg,
    /// <summary>The page to read: no editing, no edit outline, no spelling marks. Blocks can be
    /// selected and copied; a double-click or Enter edits one, in WYSIWYG. Files opened from
    /// outside the app (Explorer, the command line) open in it.</summary>
    Read,
}
