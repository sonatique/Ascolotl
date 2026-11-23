using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ascolotl.Core.Spelling;

namespace Ascolotl.App.Views.Assist;

/// <summary>
/// The rendered page's misspelled words, underlined as in the source: the text of its blocks as
/// shown, inline code and code blocks left out, checked in the background after each render.
/// Laid over the page, the same size as it.
/// </summary>
internal sealed class PageSpelling : Control
{
    private (Control? Root, FontFamily? Code, string Language) _page;
    private List<(TextBlock Text, int Start, int Length)> _words = [];
    private List<Rect> _rects = [];
    private object? _placed;
    private int _check; // the latest check: an older one's result comes too late

    public PageSpelling()
    {
        IsHitTestVisible = false;
        ZIndex = 1;
    }

    /// <summary>Checks the page's text in <paramref name="language"/>; <paramref name="code"/> is the code font.</summary>
    public async void Check(Control? root, FontFamily code, string language)
    {
        var check = ++_check;
        _page = (root, code, language);
        if (!SpellCheck.Enabled || root is null)
        {
            Show([]);
            return;
        }
        var texts = root.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !Equals(t.FontFamily, code) && !IsCodeLabel(t, code))
            .Select(t => (Text: t, Shown: ShownText(t, code))).ToList();
        List<(TextBlock, int, int)>? found;
        try
        {
            found = await Task.Run(() =>
            {
                if (Speller.For(language) is not { } speller) return null;
                var known = new Dictionary<string, bool>(); // a word comes back many times
                return texts.SelectMany(t => SpellingWords.InShownText(t.Shown)
                        .Where(w => check == _check && !Known(t.Shown.Substring(w.Start, w.Length)))
                        .Select(w => (t.Text, w.Start, w.Length)))
                    .ToList();

                bool Known(string word) => known.TryGetValue(word, out var k) ? k : known[word] = speller.IsKnown(word);
            });
        }
        catch (Exception)
        {
            return; // a dictionary that can't be read: nothing marked, never the app down
        }
        if (check == _check) Show(found ?? []);
    }

    /// <summary>The text as the text block lays it out (an embedded control is one character), with
    /// the runs in <paramref name="blank"/>'s font, if given, blanked.</summary>
    internal static string ShownText(TextBlock text, FontFamily? blank = null)
    {
        if (text.Inlines is not { Count: > 0 } inlines) return text.Text ?? "";
        var shown = new StringBuilder();
        void Add(InlineCollection all)
        {
            foreach (var inline in all)
                switch (inline)
                {
                    case Run run when blank is not null && Equals(run.FontFamily, blank): shown.Append(' ', run.Text?.Length ?? 0); break;
                    case Run run: shown.Append(run.Text); break;
                    case Span span: Add(span.Inlines); break;
                    case LineBreak: shown.Append(Environment.NewLine); break;
                    default: shown.Append(' '); break;
                }
        }
        Add(inlines);
        return shown.ToString();
    }

    /// <summary>A code block's language, shown above its code.</summary>
    private static bool IsCodeLabel(TextBlock text, FontFamily code) =>
        text.GetVisualParent() is Panel panel && panel.Children.Any(c => c is TextBlock t && Equals(t.FontFamily, code));

    /// <summary>Where <paramref name="range"/>, of the text block's layout, lies on <paramref name="target"/>; null: not shown.</summary>
    internal static Rect? On(TextBlock text, Rect range, Visual target)
    {
        var padding = new Vector(text.Padding.Left, text.Padding.Top);
        return text.IsEffectivelyVisible && text.TranslatePoint(range.TopLeft + padding, target) is { } a
            && text.TranslatePoint(range.BottomRight + padding, target) is { } b ? new Rect(a, b) : null;
    }

    private void Show(List<(TextBlock, int, int)> words)
    {
        _words = words;
        Place(_page.Root, null);
    }

    /// <summary>Lays the underlines on the page shown. Each layout pass, each scroll step too, asks:
    /// they move only with the page's size, the open block or the words.</summary>
    public void Place(Control? root, object? openBlock)
    {
        var placed = (Bounds, root, root?.Bounds, openBlock, _words);
        if (Equals(placed, _placed)) return;
        _placed = placed;
        var rects = new List<Rect>();
        if (root == _page.Root)
            foreach (var (text, start, length) in _words)
                foreach (var range in text.TextLayout.HitTestTextRange(start, length))
                    if (On(text, range, this) is { } r)
                        rects.Add(r);
        if (rects.SequenceEqual(_rects)) return;
        _rects = rects;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        foreach (var rect in _rects)
            DiagnosticRenderer.DrawSquiggle(context, SpellingController.Pen, rect);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SpellCheck.Changed += AppWide;
        Speller.Personal.Changed += AppWide;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SpellCheck.Changed -= AppWide;
        Speller.Personal.Changed -= AppWide;
        _check++; // a check on its way is dropped
    }

    // Raised on whatever thread changed it: the page is only touched on its own.
    private void AppWide() => Dispatcher.UIThread.Post(() =>
    {
        if (_page.Code is { } code && this.GetVisualRoot() is not null) Check(_page.Root, code, _page.Language);
    });

    /// <summary>The misspelled words marked now, for tests.</summary>
    internal IEnumerable<string> Marked => _words.Select(w => ShownText(w.Text).Substring(w.Start, w.Length));

    /// <summary>The underlines drawn now, for tests.</summary>
    internal int Underlines => _rects.Count;
}
