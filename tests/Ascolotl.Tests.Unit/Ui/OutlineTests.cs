using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>A single click on an outline row goes to that section.</summary>
public sealed class OutlineTests
{
    private static readonly string Words = string.Concat(Enumerable.Repeat("Some words.\n\n", 60));

    // Text after "Two" too, so the page can scroll it to the top.
    private static readonly string Text = "= Doc\n\n== One\n\n" + Words + "== Two\n\n" + Words;

    private static (Window Window, MainWindowViewModel Vm) OpenWithOutline(EditorMode mode)
    {
        var (window, vm) = Open(Text, mode);
        vm.ShowOutlinePane = true;
        Pump(window);
        return (window, vm);
    }

    private static void ClickOutlineRow(Window window, string title)
    {
        var row = window.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(i => i.DataContext is OutlineItem item && item.Title == title);
        var p = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Pump(window);
    }

    [AvaloniaFact]
    public void Wysiwyg_ScrollsThePageToTheSection()
    {
        var (window, _) = OpenWithOutline(EditorMode.Wysiwyg);
        var scroll = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;
        Assert.Equal(0, scroll.Offset.Y);

        ClickOutlineRow(window, "Two");

        var heading = Preview(window).GetVisualDescendants().OfType<TextBlock>().First(t => (t.Inlines is { Count: > 0 } i ? i.Text : t.Text) == "Two");
        var top = heading.TranslatePoint(default, scroll)!.Value.Y;
        Assert.True(scroll.Offset.Y > 0, "the page should have scrolled");
        Assert.InRange(top, -1, 40);
    }

    [AvaloniaFact]
    public void Split_MovesTheCaretToTheSection()
    {
        var (window, vm) = OpenWithOutline(EditorMode.Split);
        var line = vm.OutlineItems.Single(i => i.Title == "Two").LineNumber;

        ClickOutlineRow(window, "Two");

        var editor = window.GetVisualDescendants().OfType<TextEditor>().First(e => e.IsEffectivelyVisible);
        Assert.Equal(line + 1, editor.TextArea.Caret.Line);
    }
}
