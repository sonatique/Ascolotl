using Ascolotl.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
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

        Click(EditorMode.Source);
        Assert.False(window.FindControl<Control>("FormatBar")!.IsEffectivelyVisible);
        Assert.True(modeSwitch.IsEffectivelyVisible); // still there to leave Source
        Click(EditorMode.Wysiwyg);
        Click(EditorMode.Split);
    }
}
