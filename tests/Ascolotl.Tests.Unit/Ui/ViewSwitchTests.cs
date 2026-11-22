using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Avalonia;
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

    [AvaloniaFact]
    public void Wysiwyg_WorksWithoutAMouse()
    {
        var (window, _) = Open(string.Concat(Enumerable.Range(1, 200).Select(i => $"Paragraph {i}.\n\n")), EditorMode.Split);
        window.GetVisualDescendants().OfType<TextEditor>().First().TextArea.Focus(); // typing in the source
        window.KeyPress(Key.D3, RawInputModifiers.Control, PhysicalKey.Digit3, "3");
        Pump(window);
        var page = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;
        Assert.True(page.IsFocused); // the hidden source handed it the keyboard

        window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Pump(window);
        var paged = page.Offset.Y;
        Assert.True(paged > 0);
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Pump(window);
        Assert.True(page.Offset.Y > paged);
        window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
        Pump(window);
        Assert.Equal(0, page.Offset.Y);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Pump(window);
        Assert.Equal("Paragraph 1.", Preview(window).GetVisualDescendants().OfType<TextEditor>().Single().Text.TrimEnd());
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump(window);
        Assert.Empty(Preview(window).GetVisualDescendants().OfType<TextEditor>());
        Assert.True(page.IsFocused); // still on the page for the next key

        ContextMenu? opened = null;
        using (ContextMenu.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) => opened = menu))
        {
            window.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, null);
            Pump(window);
        }
        Assert.NotNull(opened); // the block menu, as on a right-click
        opened.Close();
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
