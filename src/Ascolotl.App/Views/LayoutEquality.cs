using AdocNet;
using AdocNet.Layout;

namespace Ascolotl.App.Views;

/// <summary>
/// Whether two layout blocks render the same, wherever they sit in the source:
/// source ranges are compared relative to their block's first line, so a block
/// that only moved (lines added or removed above it) still matches. Ranges never
/// change the rendering, only click-to-source, so an identical range matches as well.
/// Unknown layout kinds never match, so a newer AdocNet can only cost a re-render.
/// </summary>
public sealed class LayoutEquality
{
    private readonly int _aBase, _bBase;

    private LayoutEquality(int aBase, int bBase) => (_aBase, _bBase) = (aBase, bBase);

    public static bool Same(BlockLayout a, BlockLayout b)
        => new LayoutEquality(a.Source.Start.Line, b.Source.Start.Line).Block(a, b);

    private bool Block(BlockLayout a, BlockLayout b)
    {
        if (a.GetType() != b.GetType() || !Range(a.Source, b.Source)) return false;
        return (a, b) switch
        {
            (ParagraphLayout x, ParagraphLayout y) => Inlines(x.Inlines, y.Inlines),
            (HeadingLayout x, HeadingLayout y) => x.Level == y.Level && Inlines(x.Inlines, y.Inlines),
            (CodeBlockLayout x, CodeBlockLayout y) => x.Text == y.Text && x.Language == y.Language,
            (AdmonitionLayout x, AdmonitionLayout y) => x.Kind == y.Kind && Blocks(x.Blocks, y.Blocks),
            (ListLayout x, ListLayout y) => x.Ordered == y.Ordered && All(x.Items, y.Items, Block),
            (ListItemLayout x, ListItemLayout y) => x.Checked == y.Checked && Inlines(x.Inlines, y.Inlines) && Blocks(x.Blocks, y.Blocks),
            (QuoteLayout x, QuoteLayout y) => x.Attribution == y.Attribution && x.Citation == y.Citation && Blocks(x.Blocks, y.Blocks),
            (ImageLayout x, ImageLayout y) => x.Target == y.Target && x.Alt == y.Alt && x.Title == y.Title
                && x.Width == y.Width && x.Height == y.Height,
            (DescriptionListLayout x, DescriptionListLayout y) => All(x.Items, y.Items,
                (i, j) => Inlines(i.Term, j.Term) && Inlines(i.Description, j.Description)),
            (TableLayout x, TableLayout y) => x.Title == y.Title && x.HasHeader == y.HasHeader
                && x.HasFooter == y.HasFooter && All(x.Rows, y.Rows, Row),
            (ThematicBreakLayout, ThematicBreakLayout) or (PageBreakLayout, PageBreakLayout) => true,
            _ => false,
        };
    }

    private bool Row(TableRowLayout a, TableRowLayout b) => Range(a.Source, b.Source) && All(a.Cells, b.Cells, Cell);

    private bool Cell(TableCellLayout a, TableCellLayout b)
        => Range(a.Source, b.Source) && a.ColSpan == b.ColSpan && a.RowSpan == b.RowSpan
           && a.IsHeader == b.IsHeader && Inlines(a.Inlines, b.Inlines);

    private bool Inline(InlineLayout a, InlineLayout b)
    {
        if (a.GetType() != b.GetType() || !Range(a.Source, b.Source)) return false;
        return (a, b) switch
        {
            (TextRun x, TextRun y) => x.Text == y.Text,
            (LinkRun x, LinkRun y) => x.Href == y.Href && Inlines(x.Children, y.Children),
            (BoldRun x, BoldRun y) => Inlines(x.Children, y.Children),
            (ItalicRun x, ItalicRun y) => Inlines(x.Children, y.Children),
            (MonoRun x, MonoRun y) => Inlines(x.Children, y.Children),
            (SubscriptRun x, SubscriptRun y) => Inlines(x.Children, y.Children),
            (SuperscriptRun x, SuperscriptRun y) => Inlines(x.Children, y.Children),
            (StrikethroughRun x, StrikethroughRun y) => Inlines(x.Children, y.Children),
            (HighlightRun x, HighlightRun y) => Inlines(x.Children, y.Children),
            (ImageRun x, ImageRun y) => x.Target == y.Target && x.Alt == y.Alt,
            (LineBreakRun, LineBreakRun) => true,
            _ => false,
        };
    }

    private bool Blocks(IReadOnlyList<BlockLayout> a, IReadOnlyList<BlockLayout> b) => All(a, b, Block);

    private bool Inlines(IReadOnlyList<InlineLayout> a, IReadOnlyList<InlineLayout> b) => All(a, b, Inline);

    private static bool All<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> same)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!same(a[i], b[i])) return false;
        return true;
    }

    private bool Range(SourceRange a, SourceRange b)
        => a == b
           || !a.IsNone && !b.IsNone
              && a.Start.Line - _aBase == b.Start.Line - _bBase && a.Start.Column == b.Start.Column
              && a.End.Line - _aBase == b.End.Line - _bBase && a.End.Column == b.End.Column;
}
