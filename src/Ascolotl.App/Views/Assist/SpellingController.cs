using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Ascolotl.Core.Resources;
using Ascolotl.Core.Spelling;

namespace Ascolotl.App.Views.Assist;

/// <summary>
/// Marks an editor's misspelled words with a wavy line, a pause after typing, checked in the
/// background; the marks move with the text until the next check. The right-click menu gets
/// the word's suggestions, Add to dictionary and Ignore.
/// </summary>
internal sealed class SpellingController : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Func<bool> _markdown;
    private readonly Func<string> _documentText; // the whole document: an open block's editor holds one block
    private readonly Action<string> _status;
    private TextSegmentCollection<TextSegment> _marks;
    private int _check; // the latest check: an older one's result comes too late
    private (int Start, int End)? _typing; // the word at the caret, not marked while it's being typed

    // Told once a session: no dictionary for this language.
    private static readonly HashSet<string> Told = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly Pen Pen = new(new SolidColorBrush(Color.Parse("#D94F7A")), 1.2);

    public SpellingController(TextEditor editor, Func<bool> markdown, Func<string> documentText, Action<string> status)
    {
        (_editor, _markdown, _documentText, _status) = (editor, markdown, documentText, status);
        _marks = new TextSegmentCollection<TextSegment>(editor.Document);
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
        editor.TextChanged += (_, _) => Restart();
        editor.DocumentChanged += (_, _) =>
        {
            _marks = new TextSegmentCollection<TextSegment>(editor.Document);
            Restart();
        };
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            // Off the word that was being typed: now it's checked.
            if (_typing is var (s, e) && (_editor.CaretOffset < s || _editor.CaretOffset > e)) Restart();
        };
        // App-wide events, held only while the editor is shown: an open block's editor is short-lived.
        editor.AttachedToVisualTree += (_, _) =>
        {
            Listen(true);
            Restart(); // a check stopped when it was taken off the screen
        };
        editor.DetachedFromVisualTree += (_, _) =>
        {
            Listen(false);
            _check++; // a check on its way is dropped
        };
        if (editor.GetVisualRoot() is not null) Listen(true);
        Restart();
    }

    private void Listen(bool on)
    {
        SpellCheck.Changed -= AppWide;
        Speller.Personal.Changed -= AppWide;
        if (!on) return;
        SpellCheck.Changed += AppWide;
        Speller.Personal.Changed += AppWide;
    }

    // Raised on whatever thread changed it: the editor is only touched on its own.
    private void AppWide() => Dispatcher.UIThread.Post(Restart);

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Checks again after a pause: typing on puts it off.</summary>
    private async void Restart()
    {
        var check = ++_check;
        if (!SpellCheck.Enabled)
        {
            Show([]);
            return;
        }
        await Task.Delay(500);
        if (check == _check) Check();
    }

    private string Language() => Language(_documentText(), _markdown());

    /// <summary>The document's language (its <c>:lang:</c>), else the interface's.</summary>
    internal static string Language(string document, bool markdown) => Speller.DocumentLanguage(document, markdown) ?? CultureInfo.CurrentUICulture.Name;

    private async void Check()
    {
        var check = ++_check;
        var text = _editor.Document.Text;
        var (markdown, language) = (_markdown(), Language());
        List<(int Start, int Length)>? misspelled;
        try
        {
            misspelled = await Task.Run(() => Speller.For(language) is { } speller
                ? SpellingWords.Find(text, markdown).Where(w => check == _check && !speller.IsKnown(text.Substring(w.Start, w.Length))).ToList()
                : null);
        }
        catch (Exception)
        {
            return; // a dictionary that can't be read: nothing marked, never the app down
        }
        if (check != _check) return; // the text changed meanwhile: a newer check is on its way
        if (misspelled is null)
        {
            if (Told.Add(language)) _status(string.Format(Strings.StatusNoDictionary, language, Speller.Folders.FirstOrDefault()));
            Show([]);
            return;
        }
        var caret = _editor.CaretOffset;
        _typing = misspelled.FirstOrDefault(w => caret >= w.Start && caret <= w.Start + w.Length) is var (ts, tl) && tl > 0 ? (ts, ts + tl) : null;
        Show(misspelled.Where(w => _typing is not var (s, _) || w.Start != s));
    }

    private void Show(IEnumerable<(int Start, int Length)> words)
    {
        _marks.Clear();
        foreach (var (start, length) in words)
            _marks.Add(new TextSegment { StartOffset = start, Length = length });
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_marks.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0) return;
        var (first, last) = (textView.VisualLines[0].FirstDocumentLine.Offset, textView.VisualLines[^1].LastDocumentLine.EndOffset);
        foreach (var mark in _marks.FindOverlappingSegments(first, last - first))
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, mark))
                DiagnosticRenderer.DrawSquiggle(drawingContext, Pen, rect);
    }

    /// <summary>The right-click menu's first entries for a misspelled word at the caret: its
    /// suggestions, Add to dictionary and Ignore. None elsewhere.</summary>
    public List<Control> MenuItems()
    {
        if (_marks.FindSegmentsContaining(_editor.CaretOffset).FirstOrDefault() is not { } mark) return [];
        var word = _editor.Document.GetText(mark);
        var items = new List<Control>();
        foreach (var suggestion in Speller.For(Language())?.Suggest(word) ?? [])
        {
            var item = new MenuItem { Header = suggestion, FontWeight = FontWeight.SemiBold };
            item.Click += (_, _) =>
            {
                if (mark.EndOffset > _editor.Document.TextLength || _editor.Document.GetText(mark) != word) return; // no longer the word
                _editor.Document.Replace(mark.StartOffset, mark.Length, suggestion);
                _marks.Remove(mark); // right now, not at the next check
                _editor.TextArea.TextView.InvalidateLayer(Layer);
            };
            items.Add(item);
        }
        if (items.Count == 0) items.Add(new MenuItem { Header = Strings.SpellingNoSuggestions, IsEnabled = false });
        var add = new MenuItem { Header = Strings.MenuAddToDictionary };
        add.Click += (_, _) => Speller.Personal.Add(word);
        var ignore = new MenuItem { Header = Strings.MenuIgnoreWord };
        ignore.Click += (_, _) => Speller.Personal.Ignore(word);
        items.AddRange([new Separator(), add, ignore, new Separator()]);
        return items;
    }

    /// <summary>The misspelled words marked now, for tests.</summary>
    internal IEnumerable<string> Marked => _marks.Select(m => _editor.Document.GetText(m));
}
