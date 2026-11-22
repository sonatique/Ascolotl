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
    public void TableCells_WorkWithoutAMouse()
    {
        var (window, vm) = Open("Before.\n\n|===\n| a1 | b1\n\n| a2 | b2\n|===\n", EditorMode.Wysiwyg);
        Preview(window).FocusPage();
        Pump(window);
        string Cell() => BlockEditor(window)!.Text;

        Press(window, Key.Enter);
        Press(window, Key.Down); // past the paragraph: into the table's first cell
        Assert.Equal("a1", Cell());
        Press(window, Key.Tab);
        Assert.Equal("b1", Cell());
        Press(window, Key.Tab); // on to the next row
        Assert.Equal("a2", Cell());
        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.Equal("b1", Cell());
        Assert.Equal(2, BlockEditor(window)!.CaretOffset); // coming back: at the end

        window.KeyTextInput("!");
        Press(window, Key.Down); // a change is applied on the way
        Pump(window);
        Assert.Contains("| a1 | b1!\n", vm.SelectedDocument!.SourceText);
        Assert.Equal("b2", Cell());
        Press(window, Key.Left);
        Assert.Equal("a2", Cell());
        Press(window, Key.Up);
        Assert.Equal("a1", Cell());
        Press(window, Key.Up); // out of the table, to the end of the paragraph above
        Assert.Equal("Before.", Cell().TrimEnd());
        Assert.Equal("Before.".Length, BlockEditor(window)!.CaretOffset);

        Press(window, Key.Down);
        Press(window, Key.Tab);
        Press(window, Key.Tab);
        Press(window, Key.Tab);
        Assert.Equal("b2", Cell());

        // Ctrl+Enter: a new row, its first cell open.
        Press(window, Key.Enter, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal(3, vm.SelectedDocument!.SourceText.Split('\n').Count(l => l.StartsWith("| ")));
        Assert.NotNull(BlockEditor(window));
        Assert.Equal("", Cell().Trim());
        window.KeyTextInput("33"); // digits: no completion list to take the Tab
        Press(window, Key.Tab);
        Pump(window);
        Assert.Equal("", Cell()); // an empty cell opens to type into
        window.KeyTextInput("44");

        // Past the last cell of the last block: a new paragraph, its placeholder selected.
        Press(window, Key.Tab);
        Pump(window);
        Assert.Contains("\n|33 |44\n", vm.SelectedDocument.SourceText);
        Assert.Equal("New paragraph.", Cell().TrimEnd());
        Assert.Equal("New paragraph.", BlockEditor(window)!.SelectedText.TrimEnd());
        Press(window, Key.Right);
        Press(window, Key.Down); // not a second one
        Pump(window);
        Assert.Equal(1, vm.SelectedDocument.SourceText.Split("New paragraph.").Length - 1);
    }

    [AvaloniaFact]
    public void BlockKeys_MoveDuplicateDeleteAndUndo()
    {
        var (window, vm) = Open("One.\n\n.Code\n[source]\n----\nx\n----\n\nThree.\n", EditorMode.Wysiwyg);
        string Text() => vm.SelectedDocument!.SourceText;
        Preview(window).FocusPage();
        Pump(window);
        Assert.Equal("One.", OnBlock(window));
        Press(window, Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        Pump(window);
        Assert.True(vm.SelectedDocument!.IsDirty);
        Press(window, Key.Escape);
        Pump(window);
        Assert.False(vm.SelectedDocument.IsDirty); // a new paragraph cancelled: as it was
        Assert.Equal("One.", OnBlock(window));

        Press(window, Key.Down, RawInputModifiers.Control | RawInputModifiers.Shift);
        Pump(window);
        Assert.Equal(".Code\n[source]\n----\nx\n----\n\nOne.\n\nThree.\n", Text()); // its title and style go along
        Assert.Equal("One.", OnBlock(window)); // and the keyboard with the block
        Press(window, Key.Up, RawInputModifiers.Control | RawInputModifiers.Shift);
        Pump(window);
        Assert.Equal("One.\n\n.Code\n[source]\n----\nx\n----\n\nThree.\n", Text());
        Assert.Equal("One.", OnBlock(window));

        Press(window, Key.D, RawInputModifiers.Control | RawInputModifiers.Alt);
        Pump(window);
        Assert.StartsWith("One.\n\nOne.\n\n.Code", Text());
        Press(window, Key.Down);
        Press(window, Key.Down);
        Press(window, Key.Delete);
        Pump(window);
        Assert.Equal("One.\n\nOne.\n\nThree.\n", Text()); // the code block, its title and style
        Assert.Equal("Three.", OnBlock(window));

        Press(window, Key.Z, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal("One.\n\nOne.\n\n.Code\n[source]\n----\nx\n----\n\nThree.\n", Text());
        Press(window, Key.Y, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal("One.\n\nOne.\n\nThree.\n", Text());

        Press(window, Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
        Pump(window);
        foreach (var key in "Four.") // key by key: "F" opens the block list, "Fo" leaves it empty
            window.KeyTextInput(key.ToString());
        Press(window, Key.Enter, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal("One.\n\nOne.\n\nThree.\n\nFour.", Text().TrimEnd());
        Assert.Equal("Four.", OnBlock(window));

        // Esc takes a new paragraph away again, typed into or not.
        var withFour = Text();
        foreach (var typed in new[] { "", "9", "Five" }) // "9": a list that matches nothing from the first key
        {
            Press(window, Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
            Pump(window);
            foreach (var key in typed) window.KeyTextInput(key.ToString());
            Assert.Empty(BlockEditor(window)!.TextArea.StackedInputHandlers); // no list left holding the keys
            Press(window, Key.Escape);
            Pump(window);
            Assert.Null(BlockEditor(window));
            Assert.Equal(withFour, Text());
            Assert.Equal("Four.", OnBlock(window));
        }

        // Up from below a table: its last cell.
        vm.SelectedDocument!.SourceText = "|===\n| a | b\n|===\n\nAfter.\n";
        Pump(window);
        Press(window, Key.End);
        Press(window, Key.Enter);
        Press(window, Key.Up);
        Assert.Equal("b", BlockEditor(window)!.Text);
        Assert.Equal(1, BlockEditor(window)!.CaretOffset);
    }

    [AvaloniaFact]
    public void MovingABlockAboveTheView_KeepsTheKeyboardOnIt()
    {
        var (window, vm) = Open(string.Concat(Enumerable.Range(1, 60).Select(i => $"Paragraph {i}.\n\n")), EditorMode.Wysiwyg);
        Preview(window).FocusPage();
        Pump(window);
        Press(window, Key.PageDown);
        Pump(window);
        var moved = OnBlock(window); // the first block in view
        Assert.NotEqual("Paragraph 1.", moved);

        Press(window, Key.Up, RawInputModifiers.Control | RawInputModifiers.Shift);
        Pump(window);
        Press(window, Key.Up, RawInputModifiers.Control | RawInputModifiers.Shift);
        Pump(window);
        Assert.Equal(moved, OnBlock(window)); // above the view's top: brought back into view, not swapped for another
        var n = int.Parse(moved!["Paragraph ".Length..^1]);
        Assert.Contains($"Paragraph {n}.\n\nParagraph {n - 2}.\n\nParagraph {n - 1}.", vm.SelectedDocument!.SourceText);
    }

    [AvaloniaFact]
    public void UndoOnThePage_UndoesTheShownTab_NotAnotherOne()
    {
        var (window, vm) = Open("First tab.\n\nKept.\n", EditorMode.Split);
        var first = vm.SelectedDocument!;
        Preview(window).FocusPage();
        Pump(window);
        Press(window, Key.Delete);
        Pump(window);
        vm.EditorMode = EditorMode.Wysiwyg; // the source, hidden, keeps the tab's history
        Pump(window);
        Press(window, Key.Z, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal("First tab.\n\nKept.\n", first.SourceText);

        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "One.\n\nTwo.\n";
        vm.RequestRender(immediate: true);
        Pump(window);
        Preview(window).FocusPage();
        Pump(window);

        Press(window, Key.Delete);
        Pump(window);
        Assert.Equal("Two.\n", vm.SelectedDocument.SourceText);
        Press(window, Key.Z, RawInputModifiers.Control);
        Pump(window);
        Assert.Equal("One.\n\nTwo.\n", vm.SelectedDocument.SourceText);
        Assert.Equal("First tab.\n\nKept.\n", first.SourceText);
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
