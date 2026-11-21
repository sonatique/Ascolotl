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
    public void Sidebar_SitsUnderTheToolbar_OnTheRight_AndTheTitleRowSpansTheWindow()
    {
        var (window, _) = OpenWithOutline(EditorMode.Split);
        double Top(Visual v) => v.TranslatePoint(default, window)!.Value.Y;
        double Left(Visual v) => v.TranslatePoint(default, window)!.Value.X;

        var toolbar = window.FindControl<Control>("FormatBar")!;
        var pane = window.FindControl<Border>("OutlinePane")!;
        var preview = window.FindControl<Control>("PreviewPane")!;
        Assert.True(Top(pane) >= Top(toolbar) + toolbar.Bounds.Height);
        Assert.True(Left(pane) >= Left(preview) + preview.Bounds.Width, "the sidebar is right of the preview");
        Assert.Equal(window.Bounds.Width, Left(pane) + pane.Bounds.Width, 1);
        Assert.True(window.GetVisualDescendants().OfType<Image>().First().TranslatePoint(default, window)!.Value.X < 20,
            "the app icon starts the title row at the window's left edge");
    }

    [AvaloniaFact]
    public void DraggingTheSplitter_ResizesTheSidebar_WhichKeepsItsWidthWhenHidden()
    {
        var (window, vm) = OpenWithOutline(EditorMode.Split);
        double Right(Visual v) => v.TranslatePoint(default, window)!.Value.X + v.Bounds.Width;
        var pane = window.FindControl<Border>("OutlinePane")!;
        var preview = window.FindControl<Control>("PreviewPane")!;
        var splitter = window.FindControl<GridSplitter>("SidebarSplitter")!;
        var p = splitter.TranslatePoint(new Point(2, splitter.Bounds.Height / 2), window)!.Value;

        window.MouseDown(p, MouseButton.Left);
        window.MouseMove(p - new Vector(100, 0));
        window.MouseUp(p - new Vector(100, 0), MouseButton.Left);
        Pump(window);
        Assert.Equal(320, pane.Bounds.Width, 0);
        Assert.Equal(window.Bounds.Width, Right(pane), 1);

        vm.ShowOutlinePane = false;
        Pump(window);
        Assert.Equal(window.Bounds.Width, Right(preview), 1); // a hidden sidebar takes no space

        vm.ShowOutlinePane = true;
        Pump(window);
        Assert.Equal(320, pane.Bounds.Width, 0);
    }

    [AvaloniaFact]
    public void CloseCross_HidesTheSidebar()
    {
        var (window, vm) = OpenWithOutline(EditorMode.Split);
        var close = window.FindControl<Button>("SidebarCloseButton")!;
        var p = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;

        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Pump(window);

        Assert.False(vm.ShowOutlinePane);
        Assert.False(window.FindControl<Border>("OutlinePane")!.IsVisible);
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
