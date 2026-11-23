using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Editing.Assist;
using Ascolotl.Core.Resources;
using Ascolotl.Core.ViewModels;

namespace Ascolotl.App.Views.Assist;

/// <summary>
/// Attaches authoring assist to an AvaloniaEdit <see cref="TextEditor"/>:
/// context-aware completion (auto-trigger + Ctrl+Space), snippet tab-stops,
/// smart typing (list continuation + delimiter auto-close), a live
/// status-bar syntax hint, and the formatting commands of the toolbar and the
/// right-click menu. One controller per editor surface.
/// </summary>
internal sealed class CompletionController
{
    private static readonly ConditionalWeakTable<TextEditor, CompletionController> ByEditor = new();

    private readonly TextEditor _editor;
    private readonly Action<string?>? _onHint;
    private readonly Func<string>? _documentSource;
    private CompletionWindow? _window;
    private SnippetSession? _session;

    public CompletionController(TextEditor editor, Action<string?>? onHint = null,
        Func<string>? documentSource = null)
    {
        _editor = editor;
        _onHint = onHint;
        // Source text used for document-aware completion (anchors, defined
        // attributes). Defaults to the editor's own text; the WYSIWYG inline
        // editor passes the whole document, since its editor holds one block.
        _documentSource = documentSource;

        ByEditor.AddOrUpdate(editor, this);
        editor.TextArea.TextEntered += OnTextEntered;
        editor.TextArea.ContextRequested += OnContextRequested;
        // Tunnel so we see Tab/Esc/Enter before the editor's own input handling.
        editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (_onHint is not null)
            editor.TextArea.Caret.PositionChanged += (_, _) => UpdateHint();
    }

    /// <summary>The assist attached to <paramref name="editor"/>, if any.</summary>
    public static CompletionController? For(TextEditor editor)
        => ByEditor.TryGetValue(editor, out var assist) ? assist : null;

    private bool _markdown;

    /// <summary>A Markdown document: formatting, completion and smart typing
    /// follow Markdown; the status-bar syntax hint (AsciiDoc only) is off.</summary>
    public bool Markdown
    {
        get => _markdown;
        set
        {
            if (_markdown == value) return;
            _markdown = value;
            _window?.Close();
            _session?.Finish();
            _session = null;
            UpdateHint();
        }
    }

    /// <summary>Apply <paramref name="command"/> to the selection, leaving the
    /// keyboard in the editor with the result selected (or its first field, when
    /// Tab moves through several).</summary>
    public void Format(FormatCommand command) => Apply(TextFormatting.Apply(command, _markdown, _editor.Document.Text,
        _editor.SelectionStart, _editor.SelectionLength));

    /// <summary>The caret's lines as headings of <paramref name="level"/>, or plain lines for 0.</summary>
    public void Heading(int level) => Apply(TextFormatting.Heading(_markdown, _editor.Document.Text,
        _editor.SelectionStart, _editor.SelectionLength, level));

    private void Apply(FormatEdit edit)
    {
        _window?.Close();
        _session?.Finish();
        _session = null;

        _editor.Document.Replace(edit.Start, edit.Length, edit.Replacement.Text);
        var stops = edit.Replacement.Stops;
        if (stops.Count > 1)
        {
            OnSnippetActivated(edit.Start, edit.Replacement);
            return;
        }
        if (stops.Count == 1)
            _editor.Select(edit.Start + stops[0].Start, stops[0].Length);
        else
            _editor.CaretOffset = edit.Start + edit.Replacement.Text.Length;
        _editor.TextArea.Focus();
    }

    /// <summary>The document's folder, for completing Markdown link targets.</summary>
    public Func<string?>? DocumentFolder { get; set; }

    /// <summary>Set for an in-place table cell editor, which holds only the cell's
    /// text: its table edits go to the whole document instead.</summary>
    public Action<TableOp>? CellTableEdit { get; set; }

    /// <summary>The caret is on a table row (or in a table cell's editor).</summary>
    public bool InTable
    {
        get
        {
            if (CellTableEdit is not null) return true;
            // Cheap first: a table row has a pipe.
            var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
            return _editor.Document.GetText(line).Contains('|')
                && TableEditing.IsInTable(_editor.Document.Text, _editor.CaretOffset, _markdown);
        }
    }

    /// <summary>Edit the table at the caret, as one undo step.</summary>
    public void Table(TableOp op)
    {
        if (CellTableEdit is { } cell)
        {
            cell(op);
            return;
        }
        if (TableEditing.Apply(op, _editor.Document.Text, _editor.CaretOffset, _markdown) is not { } edit)
        {
            Status(Strings.StatusEditTableSourceForLayout);
            return;
        }
        _editor.Document.Replace(edit.Start, edit.Length, edit.Text);
        _editor.CaretOffset = edit.Caret;
        _editor.TextArea.Focus();
        Status(TableDone(op));
    }

    /// <summary>The status line after a table edit.</summary>
    public static string TableDone(TableOp op) => op switch
    {
        TableOp.RowAbove or TableOp.RowBelow => Strings.StatusRowInserted,
        TableOp.DeleteRow => Strings.StatusRowDeleted,
        TableOp.ColumnLeft or TableOp.ColumnRight => Strings.StatusColumnInserted,
        TableOp.DeleteColumn => Strings.StatusColumnDeleted,
        TableOp.MergeRight or TableOp.MergeDown => Strings.StatusCellsMerged,
        _ => Strings.StatusCellSplit,
    };

    private void Status(string text)
    {
        if (TopLevel.GetTopLevel(_editor)?.DataContext is MainWindowViewModel vm)
            vm.StatusText = text;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // A right-click outside the selection moves the caret there first.
        if (e.TryGetPosition(_editor, out var point) && _editor.GetPositionFromPoint(point) is { } position)
        {
            var offset = _editor.Document.GetOffset(position.Line, position.Column);
            var (start, length) = (_editor.SelectionStart, _editor.SelectionLength);
            if (length == 0 || offset < start || offset > start + length)
            {
                _editor.TextArea.ClearSelection();
                _editor.CaretOffset = offset;
            }
        }
        Menu = FormattingMenu.Open(this, _editor);
        e.Handled = true;
    }

    /// <summary>The last right-click menu opened on this editor.</summary>
    public ContextMenu? Menu { get; private set; }

    /// <summary>True while a popup or snippet session owns the keyboard — the
    /// host's own key handling (e.g. block-edit Esc/Tab) should defer to us.</summary>
    public bool IsActive => _window is not null || _session is { Active: true };

    /// <summary>What Enter does when typing: closes a fence, continues a list or a table row. False when none applies.</summary>
    public bool SmartEnter() => HandleSmartEnter();

    /// <summary>Closes the suggestion list, if open, for a key the host acts on itself.</summary>
    public void CloseList()
    {
        _window?.Hide();
        _window = null;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_session is { Active: true }) return;
        if (string.IsNullOrEmpty(e.Text)) return;
        if (_window is not null) // already filtering live
        {
            HideIfEmpty();
            return;
        }
        ShowCompletion(force: false);
    }

    // What the open list completes, and whether the arrows moved in it.
    private AssistContextKind _windowKind;
    private bool _listMoved;

    /// <summary>A list filtered down to nothing shows nothing, yet would still own Esc and Enter.</summary>
    private void HideIfEmpty()
    {
        if (_window is not { } window || window.CompletionList.CurrentList.Count > 0) return;
        window.Hide();
        _window = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // While the popup is open, let it own navigation — but make Tab COMMIT
        // the highlighted suggestion. AvaloniaEdit otherwise lets Tab fall
        // through to a literal tab, which is the "Tab just inserts a tab"
        // surprise. Enter / arrows / Esc are handled by the window itself.
        if (_window is not null)
        {
            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown) _listMoved = true;
            // A fence's languages, none typed or picked: Enter opens the block without one.
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _windowKind == AssistContextKind.FenceLanguage
                && !_listMoved && _editor.CaretOffset == _window.StartOffset)
            {
                _window.Hide();
                _window = null;
                e.Handled = HandleSmartEnter();
                return;
            }
            if (e.Key == Key.Tab)
            {
                _window.CompletionList.RequestInsertion(e);
                e.Handled = true;
            }
            return;
        }

        if (_session is { Active: true })
        {
            switch (e.Key)
            {
                case Key.Tab when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                    _session.Previous();
                    e.Handled = true;
                    return;
                case Key.Tab:
                    _session.Next();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    _session.Finish();
                    _session = null;
                    e.Handled = true;
                    return;
                case Key.Enter:
                    // End the session but let the newline through.
                    _session.Finish();
                    _session = null;
                    return;
            }
        }

        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowCompletion(force: true);
            e.Handled = true;
            return;
        }

        // Inside a table, Tab adds the next cell (spreadsheet-style) rather than
        // opening completion.
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && !_markdown && TryTableCell())
        {
            e.Handled = true;
            return;
        }

        // Tab also OPENS completion when the caret has something to complete (a
        // block menu on an empty line, an attribute/macro/anchor prefix, …).
        // When there's nothing to suggest, fall through to a normal tab.
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            if (TryTriggerWithTab())
            {
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (HandleSmartEnter())
                e.Handled = true;
        }
    }

    /// <summary>Inside a table, on a cell row, Tab inserts the next cell
    /// separator (" | "). Returns false otherwise so other Tab handling runs.</summary>
    private bool TryTableCell()
    {
        var doc = _editor.Document;
        var caret = _editor.CaretOffset;
        var line = doc.GetLineByOffset(caret);
        var trimmed = doc.GetText(line.Offset, line.Length).TrimStart();

        // Only on a cell row (starts with '|'), not a delimiter line.
        if (!trimmed.StartsWith("|", StringComparison.Ordinal)
            || trimmed.StartsWith("|==", StringComparison.Ordinal))
            return false;
        if (!AsciiDocContextAnalyzer.IsInsideTable(doc.Text, caret))
            return false;

        doc.Insert(caret, " | ");
        _editor.CaretOffset = caret + 3;
        return true;
    }

    /// <summary>Tab as an explicit trigger: open completion when the caret has
    /// something completable, else return false so Tab inserts a literal tab.</summary>
    private AssistQuery Analyze()
        => _markdown
            ? MarkdownAssist.Analyze(_editor.Document.Text, _editor.CaretOffset)
            : AsciiDocContextAnalyzer.Analyze(_editor.Document.Text, _editor.CaretOffset);

    private bool TryTriggerWithTab()
    {
        var query = Analyze();
        if (query.Kind == AssistContextKind.None) return false;

        var pool = PoolFor(query);
        if (pool.Count == 0) return false;

        var allowEmpty = query.Kind is AssistContextKind.BlockStart
            or AssistContextKind.AttributeList
            or AssistContextKind.AttributeReference
            or AssistContextKind.CrossReference
            or AssistContextKind.FenceLanguage
            or AssistContextKind.LinkTarget
            or AssistContextKind.Anchor
            or AssistContextKind.ReferenceLabel;
        var hasItem = (allowEmpty && string.IsNullOrEmpty(query.Word))
            || pool.Any(i => i.Trigger.StartsWith(query.Word, StringComparison.OrdinalIgnoreCase));
        if (!hasItem) return false;

        ShowCompletion(force: true);
        return _window is not null;
    }

    private void ShowCompletion(bool force)
    {
        if (_session is { Active: true } && !force) return;

        var query = Analyze();
        if (query.Kind == AssistContextKind.None) return;
        if (!force && !ShouldAutoTrigger(query)) return;

        var pool = PoolFor(query);
        if (pool.Count == 0) return;

        // Positioning the popup reads the caret's VISUAL position. On a freshly
        // created (e.g. empty) document the text view may not have built its
        // visual lines yet, and the CompletionWindow ctor throws. Force the
        // lines first, and guard the whole show so a positioning failure can
        // never crash the app — completion just retries on the next keystroke.
        try
        {
            _editor.TextArea.TextView.EnsureVisualLines();

            var window = new CompletionWindow(_editor.TextArea)
            {
                StartOffset = query.ReplaceStart,
                CloseAutomatically = true,
            };

            foreach (var item in pool)
                window.CompletionList.CompletionData.Add(new AsciiDocCompletionData(item, OnSnippetActivated));

            if (!string.IsNullOrEmpty(query.Word))
            {
                window.CompletionList.SelectItem(query.Word);
                if (window.CompletionList.CurrentList.Count == 0)
                {
                    window.Hide(); // nothing matches the word: not shown, its hold on the editor let go
                    return;
                }
            }

            window.Closed += (_, _) => _window = null;
            _window = window;
            (_windowKind, _listMoved) = (query.Kind, false);
            window.Show();
        }
        catch (InvalidOperationException)
        {
            _window = null;
        }
    }

    private bool ShouldAutoTrigger(AssistQuery query) => query.Kind switch
    {
        // In Markdown, block starters wait for Tab or Ctrl+Space: prose lines
        // start with words too. A table cell holds no blocks, and Tab goes to the next cell.
        AssistContextKind.BlockStart => !_markdown && CellTableEdit is null && query.Word.Length >= 1,
        AssistContextKind.FenceLanguage or AssistContextKind.LinkTarget
            or AssistContextKind.Anchor or AssistContextKind.ReferenceLabel => true,
        AssistContextKind.AttributeList => true,
        AssistContextKind.AttributeReference => true,
        AssistContextKind.CrossReference => true,
        // Inline prose is noisy — only fire when the typed token actually
        // prefixes a known inline macro/markup (e.g. "xr" → xref).
        AssistContextKind.Inline => query.Word.Length >= 2
            && PoolFor(query)
                .Any(i => i.Trigger.StartsWith(query.Word, StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    /// <summary>
    /// Resolves the candidate items for a context, merging document-aware
    /// completions (anchors for cross-references, attributes defined in the
    /// document) with the static catalog.
    /// </summary>
    private IReadOnlyList<AssistItem> PoolFor(AssistQuery query)
    {
        if (_markdown)
            return MarkdownAssist.PoolFor(query, DocumentSource(), DocumentFolder?.Invoke());
        switch (query.Kind)
        {
            case AssistContextKind.CrossReference:
                return AsciiDocDocScanner.Scan(DocumentSource()).Anchors;

            case AssistContextKind.AttributeReference:
            {
                var defined = AsciiDocDocScanner.Scan(DocumentSource()).Attributes;
                var builtins = AsciiDocAssistCatalog.PoolFor(query);
                if (defined.Count == 0) return builtins;
                // Document-defined attributes first, then built-ins not already
                // shadowed by one of them.
                var seen = new HashSet<string>(
                    defined.Select(d => d.Trigger), StringComparer.OrdinalIgnoreCase);
                return defined.Concat(builtins.Where(b => seen.Add(b.Trigger))).ToList();
            }

            default:
                return AsciiDocAssistCatalog.PoolFor(query);
        }
    }

    private string DocumentSource() => _documentSource?.Invoke() ?? _editor.Document.Text;

    private void OnSnippetActivated(int baseOffset, ParsedSnippet snippet)
    {
        _session?.Finish();
        if (snippet.Stops.Count == 0)
        {
            _editor.CaretOffset = baseOffset + snippet.Text.Length;
            _session = null;
            return;
        }
        _session = new SnippetSession(_editor, baseOffset, snippet);

        // The completion popup may have held keyboard focus; return it to the
        // editor (once the popup has finished closing) so the snippet's Tab
        // navigation is caught here rather than triggering focus traversal to
        // the next control. Re-assert the first stop's selection after focus.
        var session = _session;
        Dispatcher.UIThread.Post(() =>
        {
            _editor.TextArea.Focus();
            session.Reselect();
        }, DispatcherPriority.Input);
    }

    /// <summary>Continue a list or auto-close a delimited block on Enter.</summary>
    private bool HandleSmartEnter()
    {
        var doc = _editor.Document;
        var caret = _editor.CaretOffset;
        var line = doc.GetLineByOffset(caret);
        var lineText = doc.GetText(line.Offset, line.Length);
        var prefix = doc.GetText(line.Offset, caret - line.Offset);

        if (_markdown)
            return MarkdownEnter(doc, caret, line, lineText, prefix);

        // Table: Enter on a cell row starts a new row.
        var lineTrimmed = lineText.TrimStart();
        if (lineTrimmed.StartsWith("|", StringComparison.Ordinal)
            && !lineTrimmed.StartsWith("|==", StringComparison.Ordinal)
            && AsciiDocContextAnalyzer.IsInsideTable(doc.Text, caret))
        {
            doc.Insert(caret, "\n| ");
            _editor.CaretOffset = caret + 3;
            return true;
        }

        // Delimited block: only when the caret is at the end of the opener line.
        if (caret == line.EndOffset)
        {
            var closer = SmartTyping.ClosingDelimiterFor(lineText, doc.Text);
            if (closer is not null)
            {
                doc.Insert(caret, "\n\n" + closer);
                _editor.CaretOffset = caret + 1; // the blank line between fences
                return true;
            }
        }

        var cont = SmartTyping.ContinueList(prefix);
        if (cont is null) return false;

        if (cont.TerminateEmpty)
        {
            // Drop the lone marker and break out of the list.
            doc.Replace(line.Offset, caret - line.Offset, string.Empty);
            doc.Insert(line.Offset, "\n");
            _editor.CaretOffset = line.Offset + 1;
            return true;
        }

        doc.Insert(caret, "\n" + cont.Marker);
        _editor.CaretOffset = caret + 1 + cont.Marker.Length;
        return true;
    }

    /// <summary>Markdown's Enter: close a code fence, continue a list or quote.</summary>
    private bool MarkdownEnter(AvaloniaEdit.Document.TextDocument doc, int caret,
        AvaloniaEdit.Document.DocumentLine line, string lineText, string prefix)
    {
        if (caret == line.EndOffset && MarkdownAssist.ClosingFenceFor(lineText, doc.Text) is { } fence)
        {
            doc.Insert(caret, "\n\n" + fence);
            _editor.CaretOffset = caret + 1;
            return true;
        }
        if (MarkdownAssist.ContinueList(prefix) is not { } cont) return false;
        if (cont.TerminateEmpty)
        {
            // An empty item or quote line ends the list or quote.
            doc.Replace(line.Offset, caret - line.Offset, string.Empty);
            doc.Insert(line.Offset, "\n");
            _editor.CaretOffset = line.Offset + 1;
            return true;
        }
        doc.Insert(caret, "\n" + cont.Marker);
        _editor.CaretOffset = caret + 1 + cont.Marker.Length;
        return true;
    }

    private void UpdateHint()
    {
        var hint = _markdown ? null : SyntaxHint.Describe(_editor.Document.Text, _editor.CaretOffset);
        _onHint?.Invoke(hint);
    }
}
