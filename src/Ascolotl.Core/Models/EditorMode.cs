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
}
