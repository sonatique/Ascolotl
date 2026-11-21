namespace Ascolotl.Tests.Unit.ViewModels;

using Ascolotl.Core.Editing.Assist;
using Ascolotl.Core.ViewModels;

/// <summary>
/// Tests for <see cref="SyntaxReferenceViewModel"/> — the searchable syntax
/// cheat-sheet backed by the assist catalog.
/// </summary>
public class SyntaxReferenceViewModelTests
{
    private static readonly AssistItem[] Items =
    [
        new("note", "NOTE admonition", "NOTE: x", "Single-line note.", AssistItemKind.Admonition),
        new("table", "Table", "|===", "Two-column table.", AssistItemKind.Block),
        new("xref", "Cross reference", "xref:x[]", "Cross-reference to an anchor.", AssistItemKind.Macro),
    ];

    [Fact]
    public void Filter_IgnoresAccents()
    {
        var vm = new SyntaxReferenceViewModel(
            [new("stemblock", "Bloc STEM", "[stem]", "Équation STEM/LaTeX en bloc.", AssistItemKind.Block)]);

        vm.FilterText = "equation";
        Assert.Single(vm.FilteredItems);
    }

    [Fact]
    public void MarkdownTab_OpensOnTheMarkdownSyntax_AndBackToAsciiDoc()
    {
        var vm = new SyntaxReferenceViewModel();

        vm.Open(markdown: true);
        Assert.Contains(vm.FilteredItems, i => i.Snippet.StartsWith("## "));
        Assert.DoesNotContain(vm.FilteredItems, i => i.Snippet.StartsWith("NOTE:"));
        Assert.Contains("Markdown", vm.Watermark);

        vm.Close();
        vm.Open();
        Assert.Contains(vm.FilteredItems, i => i.Snippet.StartsWith("NOTE:"));
    }

    // Each Markdown entry, as the parser reads it: the export shows the construct, not the marks.
    [Theory]
    [InlineData("h2", "<h2")]
    [InlineData("bold", "<strong>")]
    [InlineData("italic", "<em>")]
    [InlineData("mono", "<code>")]
    [InlineData("link", "<a href")]
    [InlineData("image", "<img")]
    [InlineData("ulist", "<ul")]
    [InlineData("olist", "<ol")]
    [InlineData("checklist", "class=\"checklist\"")]
    [InlineData("quote", "<blockquote")]
    [InlineData("source", "<pre")]
    [InlineData("table", "<table")]
    [InlineData("rule", "<hr")]
    public void MarkdownEntries_RenderAsTheirConstruct(string trigger, string html)
    {
        var item = MarkdownAssistCatalog.ReferenceItems.Single(i => i.Trigger == trigger);
        var parsed = Ascolotl.Core.Services.DocumentParser.Parse(
            "Before.\n\n" + Snippet.Parse(item.Snippet).Text, markdown: true, basePath: null);
        using var output = new MemoryStream();
        new AdocNet.Converters.Html.HtmlRenderer().Render(parsed.Document, output, new AdocNet.Converters.Html.HtmlRenderOptions());

        Assert.Contains(html, System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void MarkdownEntries_ShowTheirMarkdown_AndCoverEveryTrigger()
    {
        var items = MarkdownAssistCatalog.ReferenceItems;
        Assert.Equal("## Heading", items.Single(i => i.Trigger == "h2").Description);
        Assert.Equal(items.Count, items.Select(i => i.Trigger).Distinct().Count());
    }

    [Fact]
    public void DefaultCatalog_IsNotEmpty()
    {
        var vm = new SyntaxReferenceViewModel();
        Assert.True(vm.FilteredItems.Count > 20);
    }

    [Fact]
    public void Filter_MatchesLabelTriggerAndDescription()
    {
        var vm = new SyntaxReferenceViewModel(Items);

        vm.FilterText = "NOTE";
        Assert.Equal(["note"], vm.FilteredItems.Select(i => i.Trigger));

        vm.FilterText = "anchor";                 // matches xref's description
        Assert.Equal(["xref"], vm.FilteredItems.Select(i => i.Trigger));

        vm.FilterText = "";
        Assert.Equal(3, vm.FilteredItems.Count);
    }

    [Fact]
    public void Open_ResetsFilterAndSelection()
    {
        var vm = new SyntaxReferenceViewModel(Items) { FilterText = "table" };
        vm.Open();
        Assert.True(vm.IsOpen);
        Assert.Equal(string.Empty, vm.FilterText);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void Selection_NavigatesAndClamps()
    {
        var vm = new SyntaxReferenceViewModel(Items);
        vm.MoveUp();
        Assert.Equal(0, vm.SelectedIndex);
        vm.MoveDown();
        vm.MoveDown();
        vm.MoveDown(); // clamped at last
        Assert.Equal(2, vm.SelectedIndex);
        Assert.Equal("xref", vm.SelectedItem!.Trigger);
    }

    [Fact]
    public void NoMatch_SelectedItemIsNull()
    {
        var vm = new SyntaxReferenceViewModel(Items) { FilterText = "zzzz" };
        Assert.Empty(vm.FilteredItems);
        Assert.Null(vm.SelectedItem);
    }
}
