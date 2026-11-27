using AvaloniaEdit;
using AvaloniaEdit.Document;
using Ascolotl.Core.Editing.Assist;

namespace Ascolotl.App.Views.Assist;

/// <summary>
/// Drives tab-stop navigation through an expanded snippet. Each stop is tracked
/// by a pair of <see cref="TextAnchor"/>s so the segments survive edits as the
/// user fills earlier placeholders. Tab/Shift+Tab move between stops; reaching
/// the final <c>$0</c> stop (or running off the end) ends the session.
/// </summary>
internal sealed class SnippetSession
{
    private readonly TextEditor _editor;
    private readonly TextDocument _document;
    private readonly List<(TextAnchor Start, TextAnchor End, int Index)> _stops = new();
    private int _index = -1;
    private bool _active;

    /// <summary>Tab moves through the stops. False once the snippet's text is undone, deleted or
    /// replaced, or the caret has left it: Tab is the editor's own again.</summary>
    public bool Active
    {
        get
        {
            if (_active && !(Intact() && _editor.CaretOffset >= _stops.Min(s => s.Start.Offset)
                                      && _editor.CaretOffset <= _stops.Max(s => s.End.Offset)))
                _active = false;
            return _active;
        }
    }

    public SnippetSession(TextEditor editor, int baseOffset, ParsedSnippet snippet)
    {
        _editor = editor;
        _document = editor.Document;

        var docLength = editor.Document.TextLength;
        foreach (var s in snippet.Stops)
        {
            // Clamp defensively — a snippet session must never throw, whatever
            // the caller passed for baseOffset.
            var startOffset = Math.Clamp(baseOffset + s.Start, 0, docLength);
            var endOffset = Math.Clamp(baseOffset + s.Start + s.Length, 0, docLength);

            var start = editor.Document.CreateAnchor(startOffset);
            start.MovementType = AnchorMovementType.BeforeInsertion;
            var end = editor.Document.CreateAnchor(endOffset);
            end.MovementType = AnchorMovementType.AfterInsertion;
            _stops.Add((start, end, s.Index));
        }

        if (_stops.Count > 0)
        {
            _active = true;
            MoveTo(0);
        }
    }

    public void Next()
    {
        if (!Active) return;
        if (_index + 1 >= _stops.Count) { Finish(); return; }
        MoveTo(_index + 1);
    }

    public void Previous()
    {
        if (!Active) return;
        if (_index <= 0) return;
        MoveTo(_index - 1);
    }

    public void Finish() => _active = false;

    private bool Intact() => _editor.Document == _document && _stops.TrueForAll(s => !s.Start.IsDeleted && !s.End.IsDeleted);

    /// <summary>Re-apply the selection for the current stop — used after focus
    /// is returned to the editor when the snippet first opens.</summary>
    public void Reselect()
    {
        if (Active && _index >= 0)
            MoveTo(_index);
    }

    private void MoveTo(int i)
    {
        if (!Intact())
        {
            Finish();
            return;
        }
        _index = i;
        var (start, end, index) = _stops[i];
        var s = start.Offset;
        var len = Math.Max(0, end.Offset - s);

        _editor.Select(s, len);
        _editor.CaretOffset = s + len;

        // The $0 stop is the exit: place the caret and end the session.
        if (index == 0)
            Finish();
    }
}
