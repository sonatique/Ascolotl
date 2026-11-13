using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using Ascolotl.App.Views.Assist;
using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;

namespace Ascolotl.App.Views;

public partial class SourceEditorView : UserControl
{
    private DocumentViewModel? _currentDocument;
    private bool _updatingFromEditor;
    private readonly DiagnosticRenderer _diagnosticRenderer = new();
    private readonly CompletionController _assist;

    public SourceEditorView()
    {
        InitializeComponent();
        SearchPanel.Install(Editor);
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        Editor.TextArea.TextView.PointerMoved += OnEditorPointerMoved;

        // Authoring assist: completion, snippet tab-stops, smart typing, and the
        // status-bar syntax hint (pushed onto the active document).
        _assist = new CompletionController(Editor, hint =>
        {
            if (_currentDocument is not null)
                _currentDocument.SyntaxHint = hint ?? string.Empty;
        });

        if (Application.Current is not null)
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;

        DataContextChanged += OnDataContextChanged;
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

        // Set initial Vibrant Gill theme colors based on current theme
        OnThemeChanged(null, EventArgs.Empty);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        Editor.LineNumbersForeground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse(isDark ? "#9E958C" : "#7A7168"));
        Editor.TextArea.TextView.CurrentLineBorder = new Avalonia.Media.Pen(
            new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse(isDark ? "#40E8608A" : "#40D94F7A")), 1);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentDocument is not null)
        {
            _currentDocument.PropertyChanged -= OnDocumentPropertyChanged;
        }

        _currentDocument = DataContext as DocumentViewModel;

        if (_currentDocument is not null)
        {
            _currentDocument.PropertyChanged += OnDocumentPropertyChanged;
            LoadTextFromDocument(_currentDocument);
            Editor.FontSize = _currentDocument.FontSize;
            UpdateDiagnostics(_currentDocument);
        }
    }

    private void UpdateDiagnostics(DocumentViewModel doc)
    {
        _diagnosticRenderer.Diagnostics = doc.Diagnostics;
        Editor.TextArea.TextView.InvalidateVisual();
    }

    // Show the diagnostic message(s) for the line under the pointer.
    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentDocument is null || !_currentDocument.HasDiagnostics)
        {
            if (ToolTip.GetIsOpen(Editor)) ToolTip.SetIsOpen(Editor, false);
            return;
        }

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        var line = position?.Line ?? -1;
        var messages = _currentDocument.Diagnostics
            .Where(d => d.Line + 1 == line)
            .Select(d =>
            {
                var head = (d.Severity == DiagnosticSeverity.Error ? "✖ " : "⚠ ") + d.Message;
                return d.Hint is { } hint ? $"{head}\n    → {hint}" : head;
            })
            .ToList();

        if (messages.Count == 0)
        {
            if (ToolTip.GetIsOpen(Editor)) ToolTip.SetIsOpen(Editor, false);
            return;
        }

        ToolTip.SetTip(Editor, string.Join("\n", messages));
        ToolTip.SetIsOpen(Editor, true);
    }

    private void LoadTextFromDocument(DocumentViewModel doc)
    {
        _updatingFromEditor = true;
        Editor.Text = doc.SourceText;
        _updatingFromEditor = false;

        // AvaloniaEdit's LineNumberMargin sometimes ends up displaying only
        // the last digit of each line number (clipped from the left) when
        // word-wrap is enabled and the document is large. Forcing a re-measure
        // after the document has been replaced fixes it. We also set a
        // generous MinWidth so we never lose digits to a too-tight measure.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var margin in Editor.TextArea.LeftMargins.OfType<Control>())
            {
                margin.MinWidth = 48;
                margin.InvalidateMeasure();
            }
            Editor.TextArea.InvalidateMeasure();
            Editor.TextArea.TextView.InvalidateMeasure();
        }, DispatcherPriority.Background);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromEditor || _currentDocument is null)
            return;

        _currentDocument.SourceText = Editor.Text ?? string.Empty;
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_currentDocument is null)
            return;

        switch (e.PropertyName)
        {
            case nameof(DocumentViewModel.SourceText):
                if (!_updatingFromEditor && Editor.Text != _currentDocument.SourceText)
                {
                    LoadTextFromDocument(_currentDocument);
                }
                break;

            case nameof(DocumentViewModel.FontSize):
                Editor.FontSize = _currentDocument.FontSize;
                break;

            case nameof(DocumentViewModel.Diagnostics):
                UpdateDiagnostics(_currentDocument);
                break;
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_currentDocument is null)
            return;

        _currentDocument.CursorLine = Editor.TextArea.Caret.Line;
        _currentDocument.CursorColumn = Editor.TextArea.Caret.Column;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is not null)
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
    }

    /// <summary>
    /// Gives keyboard focus to the editor with the caret at the start, so the
    /// first keystroke lands in the document (used for a freshly created doc).
    /// </summary>
    public void FocusEditor()
    {
        Editor.CaretOffset = 0;
        // Focus the TextArea (the actual input surface), not just the wrapper —
        // otherwise the first keystroke isn't routed into the document.
        Editor.TextArea.Focus();
    }

    /// <summary>Insert text at the caret and focus the editor — used by the
    /// syntax-reference panel.</summary>
    public void InsertAtCaret(string text)
    {
        var offset = Editor.CaretOffset;
        Editor.Document.Insert(offset, text);
        Editor.CaretOffset = offset + text.Length;
        Editor.TextArea.Focus();
    }

    /// <summary>
    /// Scrolls to the specified line number (0-based) and positions the caret.
    /// </summary>
    public void ScrollToLine(int lineNumber)
    {
        // AvaloniaEdit uses 1-based line numbers
        var line = lineNumber + 1;
        if (line < 1) line = 1;
        if (line > Editor.Document.LineCount) line = Editor.Document.LineCount;

        var offset = Editor.Document.GetLineByNumber(line).Offset;
        Editor.CaretOffset = offset;
        Editor.ScrollTo(line, 0);
        Editor.Focus();
    }
}
