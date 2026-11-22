using Ascolotl.App.Views;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>What a screen reader announces: never a class name, never a bare glyph.</summary>
public sealed class AccessibleNameTests
{
    private static string? Announced(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();

    [AvaloniaFact]
    public void ATab_IsAnnouncedByItsTitle()
    {
        var (window, vm) = Open("= Doc\n", EditorMode.Split);
        vm.SelectedDocument!.SourceText += "x";

        var tab = window.GetVisualDescendants().OfType<TabStripItem>().Single();

        Assert.Equal(vm.SelectedDocument.TabTitle, Announced(tab)); // "Untitled 1 *", not the class name
    }

    [AvaloniaFact]
    public void TheSourceEditor_IsANamedEditField_HoldingTheText()
    {
        var (window, vm) = Open("= Doc\n\nSome words.\n", EditorMode.Split);
        var area = window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First().TextArea;

        var peer = ControlAutomationPeer.CreatePeerForElement(area);

        Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
        Assert.Equal(Strings.ModeSource, peer.GetName());
        var value = Assert.IsAssignableFrom<IValueProvider>(peer);
        Assert.Equal(vm.SelectedDocument!.SourceText, value.Value);
        Assert.False(value.IsReadOnly);
    }

    [AvaloniaFact]
    public void TheProfileDialogsGlyphButtons_SayWhatTheyDo()
    {
        var dialog = new ExportProfilesDialog();
        dialog.Show();

        Assert.Equal(Strings.AddProfile, Announced(dialog.FindControl<Button>("AddButton")!));
        Assert.Equal(Strings.RemoveSelectedProfile, Announced(dialog.FindControl<Button>("RemoveButton")!));
        Assert.Equal(Strings.DialogChooseOutputFolder, Announced(dialog.FindControl<Button>("BrowseButton")!));
        Assert.Equal(Strings.Name, Announced(dialog.FindControl<TextBox>("NameBox")!));
    }
}
