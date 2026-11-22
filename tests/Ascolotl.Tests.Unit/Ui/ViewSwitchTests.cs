using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>The toolbar's right end switches the view, in every view.</summary>
public sealed class ViewSwitchTests
{
    [AvaloniaFact]
    public void ToolbarSwitch_ChangesTheView_AndMarksTheCurrentOne()
    {
        var (window, vm) = Open("= Doc\n\nSome words.\n", EditorMode.Split);
        var modeSwitch = window.FindControl<StackPanel>("ModeSwitch")!;
        var buttons = modeSwitch.GetLogicalChildren().OfType<Button>().ToList();
        var right = modeSwitch.TranslatePoint(new Point(modeSwitch.Bounds.Width, 0), window)!.Value.X;
        Assert.InRange(right, window.Bounds.Width - 20, window.Bounds.Width); // apart from the formatting tools

        void Click(EditorMode mode)
        {
            var b = buttons.Single(b => Equals(b.CommandParameter, mode));
            var p = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window)!.Value;
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            Pump(window);
            Assert.Equal(mode, vm.EditorMode);
            Assert.Equal([mode], buttons.Where(x => x.Classes.Contains("active")).Select(x => (EditorMode)x.CommandParameter!));
        }

        Assert.Equal(["Ctrl+1", "Ctrl+2", "Ctrl+3"], buttons.Select(b => ToolTip.GetTip(b) as string)); // Source, Split, WYSIWYG
        Click(EditorMode.Source);
        Assert.False(window.FindControl<Control>("FormatBar")!.IsEffectivelyVisible);
        Assert.True(modeSwitch.IsEffectivelyVisible); // still there to leave Source
        Click(EditorMode.Wysiwyg);
        Click(EditorMode.Split);
    }

    [AvaloniaFact]
    public void CtrlDigits_SwitchTheView_ButAltGrDigitsDoNot()
    {
        var (window, vm) = Open("= Doc\n", EditorMode.Split);
        window.GetVisualDescendants().OfType<TextEditor>().First().TextArea.Focus(); // typing in the source

        window.KeyPress(Key.D1, RawInputModifiers.Control, PhysicalKey.Digit1, "1");
        Assert.Equal(EditorMode.Source, vm.EditorMode);
        window.KeyPress(Key.NumPad3, RawInputModifiers.Control, PhysicalKey.NumPad3, "3");
        Assert.Equal(EditorMode.Wysiwyg, vm.EditorMode);
        window.KeyPress(Key.D2, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.Digit2, "~");
        Assert.Equal(EditorMode.Wysiwyg, vm.EditorMode); // AltGr+2 types "~" on a French keyboard
        window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, "2");
        Assert.Equal(EditorMode.Split, vm.EditorMode);
    }

    private static void Press(Window window, Key key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPress(key, mods, PhysicalKey.None, null);
        Pump(window);
    }

    private static object? Focused(Window window) => window.FocusManager!.GetFocusedElement();

    /// <summary>What a screen reader announces for the focused block.</summary>
    private static string? OnBlock(Window window) =>
        Focused(window) is Control c && c is not TextEditor ? ControlAutomationPeer.CreatePeerForElement(c).GetName() : null;

    private static TextEditor? BlockEditor(Window window) =>
        Preview(window).GetVisualDescendants().OfType<TextEditor>().SingleOrDefault(e => !e.IsReadOnly);

    [AvaloniaFact]
    public void Wysiwyg_WorksWithoutAMouse()
    {
        var (window, vm) = Open(string.Concat(Enumerable.Range(1, 200).Select(i => $"Paragraph {i}.\n\n")), EditorMode.Split);
        window.GetVisualDescendants().OfType<TextEditor>().First().TextArea.Focus(); // typing in the source
        window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, "3");
        Pump(window);
        var page = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;
        Assert.Equal("Paragraph 1.", OnBlock(window)); // the hidden source handed the keyboard to the first block
        var keys = window.FindControl<TextBlock>("PageKeysText")!;
        Assert.True(keys.IsEffectivelyVisible); // the status bar lists the page's keys

        Press(window, Key.Down);
        Assert.Equal("Paragraph 2.", OnBlock(window));
        Press(window, Key.PageDown);
        Pump(window);
        Assert.True(page.Offset.Y > 0);
        var paged = OnBlock(window);
        Assert.NotEqual("Paragraph 2.", paged); // the keyboard followed the page
        Assert.StartsWith("Paragraph ", paged);
        Press(window, Key.End);
        Assert.Equal("Paragraph 200.", OnBlock(window));
        Press(window, Key.Home, RawInputModifiers.Control);
        Assert.Equal(0, page.Offset.Y);
        Assert.Equal("Paragraph 1.", OnBlock(window));

        // Enter edits the block; the arrows go on past its edges, to the next block's start or the previous one's end.
        Press(window, Key.Enter);
        Assert.Equal("Paragraph 1.", BlockEditor(window)!.Text.TrimEnd());
        Assert.False(keys.IsEffectivelyVisible); // the editing hint takes over
        Press(window, Key.Down);
        Assert.Equal("Paragraph 2.", BlockEditor(window)!.Text.TrimEnd());
        Assert.Equal(0, BlockEditor(window)!.CaretOffset);
        Press(window, Key.Left);
        Assert.Equal("Paragraph 1.", BlockEditor(window)!.Text.TrimEnd());
        Assert.Equal("Paragraph 1.".Length, BlockEditor(window)!.CaretOffset);

        // A change is applied on the way out.
        window.KeyTextInput("!");
        Press(window, Key.Right);
        Pump(window);
        Assert.StartsWith("Paragraph 1.!\n", vm.SelectedDocument!.SourceText);
        Assert.Equal("Paragraph 2.", BlockEditor(window)!.Text.TrimEnd());

        Press(window, Key.Escape);
        Assert.Null(BlockEditor(window));
        Assert.Equal("Paragraph 2.", OnBlock(window)); // still on the block for the next key

        // Applied with a change: the keyboard waits for the block's new rendering, then sits on it.
        Press(window, Key.F2);
        window.KeyTextInput("?");
        Press(window, Key.Enter, RawInputModifiers.Control);
        Pump(window);
        Assert.Null(BlockEditor(window));
        Assert.Equal("?Paragraph 2.", OnBlock(window));
        Press(window, Key.Up);
        Assert.Equal("Paragraph 1.!", OnBlock(window));
        Press(window, Key.Down);

        ContextMenu? opened = null;
        using (ContextMenu.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) => opened = menu))
            Press(window, Key.F10, RawInputModifiers.Shift);
        Assert.NotNull(opened); // the block menu, as on a right-click
        opened.Close();
        Pump(window);

        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.False(page.IsKeyboardFocusWithin); // Shift+Tab leaves the page
        Assert.NotNull(Focused(window));
        Press(window, Key.Tab);
        Assert.Equal("?Paragraph 2.", OnBlock(window)); // and Tab comes back to the same block
    }

    [AvaloniaFact]
    public void SwitchingTabs_KeepsTheKeyboardOnThePage()
    {
        var (window, vm) = Open("First tab.\n", EditorMode.Wysiwyg);
        var first = vm.SelectedDocument!;
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "Second tab.\n";
        vm.RequestRender(immediate: true);
        Pump(window);
        Preview(window).FocusPage();
        Pump(window);
        Assert.Equal("Second tab.", OnBlock(window));

        Press(window, Key.Tab, RawInputModifiers.Control); // to the first tab and its kept page
        Pump(window);
        Assert.Same(first, vm.SelectedDocument);
        Assert.Equal("First tab.", OnBlock(window)); // not a block of the page now hidden
        Assert.True(((Control)Focused(window)!).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void ADialog_KeepsTheKeyboard_ThePageDoesNotTakeItBack()
    {
        var (window, _) = Open("First.\n\nSecond.\n", EditorMode.Wysiwyg);
        Preview(window).FocusPage();
        Pump(window);
        Assert.Equal("First.", OnBlock(window));

        var box = new TextBox();
        var dialog = new Window { Content = box, Width = 300, Height = 200 };
        _ = dialog.ShowDialog(window);
        Pump(dialog);
        box.Focus();
        Pump(dialog);
        Pump(window);
        Assert.True(box.IsFocused);
        dialog.Close();
    }

    [AvaloniaFact]
    public void F6_MovesBetweenTheSourceAndThePage()
    {
        var (window, _) = Open("= Doc\n\nFirst.\n\nSecond.\n", EditorMode.Split);
        var source = window.GetVisualDescendants().OfType<TextEditor>().First();
        source.TextArea.Focus();

        Press(window, Key.F6);
        Assert.Equal("First.", OnBlock(window));
        Press(window, Key.F6);
        Assert.True(source.TextArea.IsFocused);
    }

    [AvaloniaFact]
    public void ABlock_TellsAScreenReaderWhatItIs()
    {
        var (window, _) = Open("= Doc\n\n== Ponds\n\nimage::pond.png[A green pond]\n", EditorMode.Wysiwyg);
        Preview(window).FocusPage();
        Pump(window);

        var heading = ControlAutomationPeer.CreatePeerForElement((Control)Focused(window)!);
        Assert.Equal("Ponds", heading.GetName());
        Assert.Equal(2, heading.GetHeadingLevel());
        Assert.True(heading.IsControlElement());
        Assert.True(heading.IsKeyboardFocusable());

        Press(window, Key.Down);
        var image = ControlAutomationPeer.CreatePeerForElement((Control)Focused(window)!);
        Assert.Equal("A green pond", image.GetName());
        Assert.Equal(AutomationControlType.Image, image.GetAutomationControlType());
        Assert.True(image.IsControlElement()); // a panel, which screen readers skip unless told otherwise
    }

    [AvaloniaFact]
    public void SyntaxHint_ShowsWithTheSource_NotInWysiwyg()
    {
        var (window, vm) = Open("= Doc\n\nSome words.\n", EditorMode.Split);
        var hint = window.FindControl<TextBlock>("SyntaxHintText")!;
        Assert.Equal(Strings.HintDocumentTitle, hint.Text); // the caret is on line 1
        Assert.True(hint.IsEffectivelyVisible);

        vm.EditorMode = EditorMode.Wysiwyg;
        Pump(window);

        Assert.False(hint.IsEffectivelyVisible); // the source it describes is hidden
    }
}
