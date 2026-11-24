using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using Ascolotl.App.Views.Assist;
using Ascolotl.Export;

namespace Ascolotl.App.Views;

/// <summary>
/// Text selected on the page in Read view, as in a word processor: from where the mouse went down
/// to where it is, across paragraphs and table cells, in reading order. Drawn over the page, the
/// same size as it; copied as text, and as HTML rebuilt from what is shown.
/// </summary>
internal sealed class PageSelection : Control
{
    /// <summary>A place in the page's text: a text block, in reading order, and a character in it.</summary>
    private readonly record struct Spot(int Text, int At)
    {
        public static bool operator <(Spot a, Spot b) => a.Text < b.Text || a.Text == b.Text && a.At < b.At;
        public static bool operator >(Spot a, Spot b) => b < a;
    }

    private List<TextBlock> _texts = [];
    private List<Rect> _boxes = [];     // each text block on this layer, for the pointer
    private HashSet<Grid> _tables = [];
    private FontFamily? _code;
    private Spot? _anchor, _end;
    private List<Rect> _rects = [];
    private object? _placed, _laidOut;

    public PageSelection()
    {
        IsHitTestVisible = false;
        ZIndex = 1;
    }

    /// <summary>The page laid out anew, <paramref name="root"/> null when it isn't being read: nothing selected.</summary>
    public void Reset(Control? root, IEnumerable<Grid> tables, FontFamily? code)
    {
        _texts = root?.GetVisualDescendants().OfType<TextBlock>().ToList() ?? [];
        _tables = [.. tables];
        _code = code;
        _anchor = _end = null;
        _placed = _laidOut = null;
        Place();
    }

    public bool HasSelection => _anchor is { } a && _end is { } b && a != b;

    /// <summary>The mouse went down at <paramref name="point"/>: the selection starts there, or with
    /// <paramref name="extend"/> (Shift), runs on to it.</summary>
    public void Begin(Point point, bool extend)
    {
        Place();
        if (SpotAt(point) is not { } at) return;
        if (!extend || _anchor is null) _anchor = at;
        _end = at;
        Place();
    }

    /// <summary>The mouse moved, its button down: the selection follows.</summary>
    public void Extend(Point point)
    {
        if (_anchor is null || SpotAt(point) is not { } at || at == _end) return;
        _end = at;
        Place();
    }

    /// <summary>A double-click: the word under <paramref name="point"/>, or the one character there if it is no word's.</summary>
    public void SelectWord(Point point)
    {
        Place();
        if (SpotAt(point) is not { } at) return;
        var shown = Shown(at.Text);
        bool InWord(int k) => k >= 0 && k < shown.Length && (char.IsLetterOrDigit(shown[k]) || shown[k] == '_');
        var c = !InWord(at.At) && InWord(at.At - 1) ? at.At - 1 : at.At; // the hit may fall just after the word
        var (start, end) = (c, Math.Min(c + 1, shown.Length));
        if (InWord(c))
        {
            while (InWord(start - 1)) start--;
            while (InWord(end)) end++;
        }
        (_anchor, _end) = (new Spot(at.Text, start), new Spot(at.Text, end));
        Place();
    }

    public void SelectAll()
    {
        if (_texts.Count == 0) return;
        _anchor = new Spot(0, 0);
        _end = new Spot(_texts.Count - 1, Shown(_texts.Count - 1).Length);
        Place();
    }

    public void Clear()
    {
        _anchor = _end = null;
        Place();
    }

    private string Shown(int i) => PageSpelling.ShownText(_texts[i]);

    /// <summary>The character nearest <paramref name="point"/>: in the text block under it, or else the
    /// nearest one, a line above or below counting for more than a block beside.</summary>
    private Spot? SpotAt(Point point)
    {
        var (best, distance) = (-1, double.MaxValue);
        for (var i = 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            if (box.Width <= 0) continue;
            var dy = point.Y < box.Top ? box.Top - point.Y : Math.Max(0, point.Y - box.Bottom);
            var dx = point.X < box.Left ? box.Left - point.X : Math.Max(0, point.X - box.Right);
            if (dy * 1000 + dx < distance) (best, distance) = (i, dy * 1000 + dx);
        }
        if (best < 0) return null;
        var text = _texts[best];
        var local = new Point(Math.Clamp(point.X - _boxes[best].X - text.Padding.Left, 0, text.TextLayout.Width),
            Math.Clamp(point.Y - _boxes[best].Y - text.Padding.Top, 0, Math.Max(0, text.TextLayout.Height - 1)));
        return new Spot(best, Math.Clamp(text.TextLayout.HitTestPoint(local).TextPosition, 0, Shown(best).Length));
    }

    /// <summary>The selection in reading order, and each text block's part of it.</summary>
    private IEnumerable<(int Text, int Start, int End)> Parts()
    {
        if (_anchor is not { } a || _end is not { } b || a == b) yield break;
        var (first, last) = a < b ? (a, b) : (b, a);
        for (var i = first.Text; i <= last.Text && i < _texts.Count; i++)
        {
            var length = Shown(i).Length;
            var start = i == first.Text ? Math.Min(first.At, length) : 0;
            var end = i == last.Text ? Math.Min(last.At, length) : length;
            if (end > start) yield return (i, start, end);
        }
    }

    /// <summary>Lays the selection on the page shown. Each layout pass asks: it moves only with the
    /// page's size or the selection.</summary>
    public void Place()
    {
        var laidOut = (Bounds, _texts.Count);
        if (!Equals(laidOut, _laidOut)) // the text blocks moved: measured again, not at each step of a drag
        {
            _laidOut = laidOut;
            _boxes = [.. _texts.Select(t => t.IsEffectivelyVisible && t.TranslatePoint(default, this) is { } o ? new Rect(o, t.Bounds.Size) : default)];
        }
        var placed = (laidOut, _anchor, _end);
        if (Equals(placed, _placed)) return;
        _placed = placed;
        var rects = new List<Rect>();
        foreach (var (i, start, end) in Parts())
            foreach (var range in _texts[i].TextLayout.HitTestTextRange(start, end - start))
                if (PageSpelling.On(_texts[i], range, this) is { } r)
                    rects.Add(r);
        if (rects.SequenceEqual(_rects)) return;
        _rects = rects;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_rects.Count == 0 || !this.TryFindResource("ForgeAccentBrush", ActualThemeVariant, out var found) || found is not IBrush accent) return;
        using (context.PushOpacity(0.28))
            foreach (var rect in _rects)
                context.DrawRectangle(accent, null, rect);
    }

    // Two text blocks side by side, as cells of a row or a list's marker and its item.
    private bool SameLine(int i, int j) =>
        Math.Min(_boxes[i].Bottom, _boxes[j].Bottom) - Math.Max(_boxes[i].Top, _boxes[j].Top) > Math.Min(_boxes[i].Height, _boxes[j].Height) / 2;

    /// <summary>The table cell a text block is in: the grid's child that holds it.</summary>
    private Control? CellOf(int i) =>
        _texts[i].GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => c.GetVisualParent() is Grid g && _tables.Contains(g));

    /// <summary>The selection as text: cells of a row apart by tabs, lines by line breaks.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            var previous = -1;
            foreach (var (i, start, end) in Parts())
            {
                if (previous >= 0)
                    text.Append(!SameLine(previous, i) ? Environment.NewLine : CellOf(i) is not null ? "\t" : " ");
                text.Append(Shown(i), start, end - start);
                previous = i;
            }
            return text.ToString();
        }
    }

    /// <summary>The selection as HTML: paragraphs, tables with their borders, and bold, italic and code.</summary>
    public string Html
    {
        get
        {
            var html = new StringBuilder();
            Control? cell = null;
            Grid? table = null;
            var (row, previous, paragraph) = (-1, -1, false);
            void CloseCell() { if (cell is not null) html.Append("</td>"); cell = null; }
            void CloseTable()
            {
                CloseCell();
                if (table is not null) html.Append("</tr></table>");
                table = null;
            }
            void CloseParagraph()
            {
                if (paragraph) html.Append("</p>");
                paragraph = false;
            }
            foreach (var (i, start, end) in Parts())
            {
                if (CellOf(i) is { } c && c.GetVisualParent() is Grid grid)
                {
                    CloseParagraph();
                    if (grid != table)
                    {
                        CloseTable();
                        html.Append(ClipboardHtml.TableAttributes).Append("><tr>");
                        (table, row) = (grid, Grid.GetRow(c));
                    }
                    else if (Grid.GetRow(c) != row)
                    {
                        CloseCell();
                        html.Append("</tr><tr>");
                        row = Grid.GetRow(c);
                    }
                    if (c != cell)
                    {
                        CloseCell();
                        html.Append("<td ").Append(ClipboardHtml.CellAttributes).Append('>');
                        cell = c;
                    }
                    else html.Append("<br>"); // a second paragraph of the same cell
                }
                else
                {
                    CloseTable();
                    if (paragraph && SameLine(previous, i)) html.Append(' '); // a list's marker and its item
                    else
                    {
                        CloseParagraph();
                        html.Append("<p>");
                        paragraph = true;
                    }
                }
                AppendInlines(html, _texts[i], start, end);
                previous = i;
            }
            CloseTable();
            CloseParagraph();
            return html.ToString();
        }
    }

    /// <summary>Characters <paramref name="start"/> to <paramref name="end"/> of the text block, as HTML, each run
    /// in its own weight, style and font.</summary>
    private void AppendInlines(StringBuilder html, TextBlock text, int start, int end)
    {
        var at = 0;
        void Add(string? content, FontWeight weight, FontStyle style, FontFamily family)
        {
            var s = content ?? "";
            var (from, to) = (Math.Max(start, at), Math.Min(end, at + s.Length));
            at += s.Length;
            if (to <= from) return;
            var piece = WebUtility.HtmlEncode(s.Substring(from - (at - s.Length), to - from)).Replace(Environment.NewLine, "<br>");
            if (_code is not null && Equals(family, _code)) piece = $"<code>{piece}</code>";
            if (style == FontStyle.Italic) piece = $"<i>{piece}</i>";
            if (weight >= FontWeight.SemiBold) piece = $"<b>{piece}</b>";
            html.Append(piece);
        }
        void Walk(InlineCollection inlines)
        {
            foreach (var inline in inlines)
                switch (inline)
                {
                    case Run run: Add(run.Text, run.FontWeight, run.FontStyle, run.FontFamily); break;
                    case Span span: Walk(span.Inlines); break;
                    case LineBreak: Add(Environment.NewLine, FontWeight.Normal, FontStyle.Normal, text.FontFamily); break;
                    default: Add(" ", FontWeight.Normal, FontStyle.Normal, text.FontFamily); break;
                }
        }
        if (text.Inlines is { Count: > 0 } all) Walk(all);
        else Add(text.Text, text.FontWeight, text.FontStyle, text.FontFamily);
    }

    /// <summary>The highlights drawn now, for tests.</summary>
    internal int Highlights => _rects.Count;
}
