using Ascolotl.Core.Models;
using Ascolotl.Tests.Unit.Mocks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>The keys the menus, the palette and the README show do what they say.</summary>
public sealed class KeyboardShortcutTests
{
    private static void TypeInTheSource(Window window) =>
        window.GetVisualDescendants().OfType<TextEditor>().First().TextArea.Focus();

    private static void Press(Window window, KeyGesture gesture)
    {
        window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, PhysicalKey.None, null);
        Pump(window);
    }

    [AvaloniaFact]
    public void EveryKeyAMenuShows_IsHandled()
    {
        var (first, _) = Open("Text.\n", EditorMode.Split);
        var gestures = first.FindControl<MenuItem>("HamburgerMenuItem")!.GetLogicalDescendants().OfType<MenuItem>()
            .Where(i => i.InputGesture is not null && i.Command is not null)
            .Select(i => i.InputGesture!).ToList();
        Assert.True(gestures.Count >= 15);

        foreach (var gesture in gestures)
        {
            var (window, _) = Open("Text.\n", EditorMode.Split);
            var handled = false;
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled |= e.Handled, RoutingStrategies.Bubble, handledEventsToo: true);
            TypeInTheSource(window);
            Press(window, gesture);
            Assert.True(handled, $"{gesture} does nothing");
        }
    }

    [AvaloniaFact]
    public void FileKeys_DoTheirJob()
    {
        var files = new MockFileService { PickSavePathResult = "/tmp/saved.adoc", SaveFileAsResult = "/tmp/saved.adoc" };
        var (window, vm) = Open("Text.\n", EditorMode.Split, files: files);
        vm.SelectedDocument!.SourceText = "Changed.\n";
        TypeInTheSource(window);

        Press(window, new KeyGesture(Key.S, KeyModifiers.Control));
        Assert.True(files.SaveFileCalled + files.SaveFileAsCalled + files.PickSavePathCalled > 0);

        var count = vm.Documents.Count;
        Press(window, new KeyGesture(Key.N, KeyModifiers.Control));
        Assert.Equal(count + 1, vm.Documents.Count);
        vm.SelectedDocument!.MarkClean(); // nothing to ask about on closing
        Press(window, new KeyGesture(Key.W, KeyModifiers.Control));
        Assert.Equal(count, vm.Documents.Count);
    }

    [AvaloniaFact]
    public void Outline_WorksFromTheKeyboard()
    {
        var (window, vm) = Open("= Doc\n\n== One\n\nText.\n\n== Two\n\nMore.\n", EditorMode.Split);
        var source = window.GetVisualDescendants().OfType<TextEditor>().First();
        var outline = window.FindControl<ListBox>("OutlineList")!;
        vm.ShowOutlinePane = false;
        Pump(window);
        source.TextArea.Focus();

        Press(window, new KeyGesture(Key.B, KeyModifiers.Control));
        Assert.True(outline.IsKeyboardFocusWithin); // the outline takes the keyboard as it opens
        Press(window, new KeyGesture(Key.Down));
        Press(window, new KeyGesture(Key.Enter));
        var section = Assert.IsType<OutlineItem>(outline.SelectedItem);
        Assert.True(source.TextArea.IsKeyboardFocusWithin); // Enter went to the section
        Assert.Equal(section.LineNumber + 1, source.TextArea.Caret.Line);

        Press(window, new KeyGesture(Key.B, KeyModifiers.Control)); // closed: the source keeps it
        Assert.True(source.TextArea.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.B, KeyModifiers.Control));
        Press(window, new KeyGesture(Key.Escape)); // and Esc goes back
        Assert.True(source.TextArea.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void MenuKeys_NeedTheirExactModifiers()
    {
        var (window, vm) = Open("Text.\n", EditorMode.Split);
        TypeInTheSource(window);
        var shown = vm.ShowOutlinePane;
        Press(window, new KeyGesture(Key.B, KeyModifiers.Control | KeyModifiers.Alt)); // AltGr+B types a character
        Assert.Equal(shown, vm.ShowOutlinePane);
        Press(window, new KeyGesture(Key.B, KeyModifiers.Control));
        Assert.NotEqual(shown, vm.ShowOutlinePane);
    }
}
