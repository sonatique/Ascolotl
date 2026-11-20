using AdocNet.Layout;
using AdocNet.Layout.Builders;
using AdocNet.Parser;
using Ascolotl.App.Views;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class LayoutEqualityTests
{
    private static IReadOnlyList<BlockLayout> Blocks(string source)
        => new LayoutBuilder().Build(AdocParser.Parse(source).Document).Children;

    [Fact]
    public void BlocksThatOnlyMoved_AreTheSame()
    {
        const string body = "* item\n+\nnested paragraph\n\nLast paragraph with *bold* and `code`.\n";
        var before = Blocks("Intro.\n\n" + body);
        var after = Blocks("Intro.\n\nAdded.\n\n" + body);

        Assert.True(LayoutEquality.Same(before[1], after[2])); // the list, with its nested paragraph
        Assert.True(LayoutEquality.Same(before[2], after[3]));
    }

    [Fact]
    public void EditedBlocks_AreNotTheSame()
    {
        Assert.False(LayoutEquality.Same(Blocks("Some text.")[0], Blocks("Some test.")[0]));
        Assert.False(LayoutEquality.Same(Blocks("Some *text*.")[0], Blocks("Some _text_.")[0]));
        Assert.False(LayoutEquality.Same(Blocks("== Title")[0], Blocks("=== Title")[0]));
    }

    /// <summary>
    /// LayoutEquality must compare every property of every layout node: one it
    /// misses (say, added by an AdocNet update) would let a changed block keep
    /// its old preview. When this fails, compare the new data there, then list it here.
    /// </summary>
    [Fact]
    public void ComparesEveryLayoutProperty()
    {
        string[] compared =
        [
            "AdmonitionLayout.Blocks", "AdmonitionLayout.Kind", "AdmonitionLayout.Source",
            "BoldRun.Children", "BoldRun.Source",
            "CodeBlockLayout.Language", "CodeBlockLayout.Source", "CodeBlockLayout.Text",
            "DescriptionItemLayout.Description", "DescriptionItemLayout.Term",
            "DescriptionListLayout.Items", "DescriptionListLayout.Source",
            "HeadingLayout.Inlines", "HeadingLayout.Level", "HeadingLayout.Source",
            "HighlightRun.Children", "HighlightRun.Source",
            "ImageLayout.Alt", "ImageLayout.Height", "ImageLayout.Source", "ImageLayout.Target",
            "ImageLayout.Title", "ImageLayout.Width",
            "ImageRun.Alt", "ImageRun.Source", "ImageRun.Target",
            "ItalicRun.Children", "ItalicRun.Source",
            "LineBreakRun.Source",
            "LinkRun.Children", "LinkRun.Href", "LinkRun.Source",
            "ListItemLayout.Blocks", "ListItemLayout.Checked", "ListItemLayout.Inlines", "ListItemLayout.Source",
            "ListLayout.Items", "ListLayout.Ordered", "ListLayout.Source",
            "MonoRun.Children", "MonoRun.Source",
            "PageBreakLayout.Source",
            "ParagraphLayout.Inlines", "ParagraphLayout.Source",
            "QuoteLayout.Attribution", "QuoteLayout.Blocks", "QuoteLayout.Citation", "QuoteLayout.Source",
            "StrikethroughRun.Children", "StrikethroughRun.Source",
            "SubscriptRun.Children", "SubscriptRun.Source",
            "SuperscriptRun.Children", "SuperscriptRun.Source",
            "TableCellLayout.ColSpan", "TableCellLayout.Inlines", "TableCellLayout.IsHeader",
            "TableCellLayout.RowSpan", "TableCellLayout.Source",
            "TableLayout.HasFooter", "TableLayout.HasHeader", "TableLayout.Rows", "TableLayout.Source",
            "TableLayout.Title",
            "TableRowLayout.Cells", "TableRowLayout.Source",
            "TextRun.Source", "TextRun.Text",
            "ThematicBreakLayout.Source",
        ];

        var actual = typeof(BlockLayout).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "AdocNet.Layout" && t.IsClass && !t.IsAbstract && t != typeof(DocumentLayout))
            .SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}"))
            .Order(StringComparer.Ordinal);

        Assert.Equal(compared.Order(StringComparer.Ordinal), actual);
    }
}
