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
