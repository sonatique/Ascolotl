using Ascolotl.App.Views;
using Ascolotl.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>In Split, scrolling the source moves the preview along.</summary>
public sealed class ScrollSyncTests
{
    private static readonly string Text =
        "= Doc\n\n" + string.Concat(Enumerable.Range(1, 120).Select(i => $"== Section {i}\n\nSome words.\n\n"));

    [AvaloniaTheory]
    [InlineData(EditorMode.Split)]
    [InlineData(EditorMode.Wysiwyg)] // the source pane didn't exist yet when the window opened
    [InlineData(EditorMode.Source)]  // nor did the preview
    public void Split_ScrollingTheSource_ScrollsThePreview(EditorMode startMode)
    {
        var (window, vm) = Open(Text, startMode);
        vm.EditorMode = EditorMode.Split;
        Pump(window);
        Assert.True(vm.SyncScrolling);
        var editor = window.GetVisualDescendants().OfType<TextEditor>().First(e => e.IsEffectivelyVisible);
        var source = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        var preview = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;

        source.Offset = new Vector(0, source.Extent.Height / 2);
        Pump(window);

        Assert.True(preview.Offset.Y > preview.Extent.Height / 4,
            $"preview offset {preview.Offset.Y} of {preview.Extent.Height} after source offset {source.Offset.Y} of {source.Extent.Height}");
    }
}
