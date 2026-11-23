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

        Press(window, new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.True(outline.IsKeyboardFocusWithin); // the outline takes the keyboard as it opens
        Press(window, new KeyGesture(Key.Down));
        Press(window, new KeyGesture(Key.Enter));
        var section = Assert.IsType<OutlineItem>(outline.SelectedItem);
        Assert.True(source.TextArea.IsKeyboardFocusWithin); // Enter went to the section
        Assert.Equal(section.LineNumber + 1, source.TextArea.Caret.Line);

        Press(window, new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift)); // closed: the source keeps it
        Assert.True(source.TextArea.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift));
        Press(window, new KeyGesture(Key.Escape)); // and Esc goes back
        Assert.True(source.TextArea.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void F8_GoesFromProblemToProblem_AndSaysWhatEachIs()
    {
        var (window, vm) = Open("One.\n\nTwo.\n\nThree.\n", EditorMode.Split);
        var source = window.GetVisualDescendants().OfType<TextEditor>().First();
        source.TextArea.Focus();
        Press(window, new KeyGesture(Key.F8));
        Assert.Equal(Ascolotl.Core.Resources.Strings.NoProblems, vm.StatusText);

        vm.SelectedDocument!.Diagnostics =
        [
            new(2, 0, 4, DiagnosticSeverity.Warning, "Second problem"),
            new(4, 0, 6, DiagnosticSeverity.Error, "Third problem"),
        ];
        Press(window, new KeyGesture(Key.F8));
        Assert.Equal(3, source.TextArea.Caret.Line);
        Assert.Contains("Second problem", vm.StatusText); // read without hovering
        Press(window, new KeyGesture(Key.F8));
        Assert.Equal(5, source.TextArea.Caret.Line);
        Press(window, new KeyGesture(Key.F8)); // round again
        Assert.Equal(3, source.TextArea.Caret.Line);
        Press(window, new KeyGesture(Key.F8, KeyModifiers.Shift));
        Assert.Equal(5, source.TextArea.Caret.Line);
        Assert.Contains("Third problem", vm.StatusText);
    }

    [AvaloniaFact]
    public void F6_GoesRoundTheSourcePageAndSidebar()
    {
        var (window, vm) = Open("= Doc\n\n== One\n\nText.\n", EditorMode.Source);
        var source = window.GetVisualDescendants().OfType<TextEditor>().First().TextArea;
        var sidebar = window.FindControl<Control>("OutlinePane")!;
        vm.ShowOutlinePane = true;
        Pump(window);
        source.Focus();

        Press(window, new KeyGesture(Key.F6)); // Tab stays in the source: F6 reaches the sidebar
        Assert.True(sidebar.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.F6));
        Assert.True(source.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.F6, KeyModifiers.Shift)); // and Shift+F6 goes back
        Assert.True(sidebar.IsKeyboardFocusWithin);

        vm.EditorMode = EditorMode.Split;
        Pump(window);
        source.Focus();
        Press(window, new KeyGesture(Key.F6));
        Assert.True(window.FindControl<Control>("PreviewPane")!.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.F6));
        Assert.True(sidebar.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.F6));
        Assert.True(source.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void FormattingKeys_Work_AndTheToolbarSaysThem()
    {
        var (window, vm) = Open("Some word here.\n", EditorMode.Split);
        var source = window.GetVisualDescendants().OfType<TextEditor>().First();
        source.TextArea.Focus();
        source.Select(5, 4); // "word"
        Press(window, new KeyGesture(Key.B, KeyModifiers.Control));
        Assert.Contains("*word*", vm.SelectedDocument!.SourceText);
        source.Select(source.Text.IndexOf("word"), 4);
        Press(window, new KeyGesture(Key.I, KeyModifiers.Control));
        Assert.Contains("_word_", vm.SelectedDocument.SourceText);

        var tips = window.GetVisualDescendants().OfType<Button>().Select(b => ToolTip.GetTip(b) as string).ToList();
        Assert.Contains(tips, t => t?.EndsWith("(Ctrl+B)") == true);
        Assert.Contains(tips, t => t?.EndsWith("(Ctrl+Shift+1)") == true);
    }

    [AvaloniaFact]
    public void HelpMenu_ShowsThePaletteTheReferenceAndTheShortcuts()
    {
        var (window, vm) = Open("Text.\n", EditorMode.Split);
        var items = window.FindControl<MenuItem>("HamburgerMenuItem")!.GetLogicalDescendants().OfType<MenuItem>().ToList();
        MenuItem Item(string header) => items.First(i => (string?)i.Header == header);
        Assert.Equal(new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift), Item(Ascolotl.Core.Resources.Strings.MenuCommandPalette).InputGesture);
        Assert.Equal(new KeyGesture(Key.F1), Item(Ascolotl.Core.Resources.Strings.MenuSyntaxReference).InputGesture);
        Assert.Equal(Ascolotl.App.Views.AppLinks.KeyboardShortcuts, Item(Ascolotl.Core.Resources.Strings.MenuKeyboardShortcuts).Tag);

        TypeInTheSource(window);
        vm.ToggleCommandPaletteCommand.Execute(null); // as the menu does: the search box still takes the keyboard
        Pump(window);
        Assert.True(window.FindControl<TextBox>("CommandPaletteFilter")!.IsFocused);
        Press(window, new KeyGesture(Key.Escape));
        Press(window, new KeyGesture(Key.F1));
        Assert.True(window.FindControl<TextBox>("SyntaxReferenceFilter")!.IsFocused);
    }

    [AvaloniaFact]
    public void PagePreview_DoesNotListKeysItTurnsOff()
    {
        var (window, vm) = Open("Paragraph 1.\n", EditorMode.Wysiwyg);
        Preview(window).FocusPage();
        Pump(window);
        var keys = window.FindControl<TextBlock>("PageKeysText")!;
        Assert.True(keys.IsEffectivelyVisible);
        vm.PagePreview = true; // pages only scroll: no Enter edit, no block menu
        Pump(window);
        Assert.False(keys.IsEffectivelyVisible);
        vm.PagePreview = false;
        Pump(window);
        Preview(window).FocusPage();
        Pump(window);
        Assert.True(keys.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void ThePaletteAndTheReference_GiveTheKeyboardBack()
    {
        var (window, _) = Open("Text.\n", EditorMode.Split);
        var source = window.GetVisualDescendants().OfType<TextEditor>().First().TextArea;
        source.Focus();
        Press(window, new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(source.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.Escape));
        Assert.True(source.IsKeyboardFocusWithin); // back where it was

        Press(window, new KeyGesture(Key.F1));
        Assert.False(source.IsKeyboardFocusWithin);
        Press(window, new KeyGesture(Key.Escape));
        Assert.True(source.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void MenuKeys_NeedTheirExactModifiers()
    {
        var (window, vm) = Open("Text.\n", EditorMode.Split);
        TypeInTheSource(window);
        var shown = vm.ShowOutlinePane;
        Press(window, new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)); // with AltGr too: not the outline's key
        Assert.Equal(shown, vm.ShowOutlinePane);
        Press(window, new KeyGesture(Key.L, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.NotEqual(shown, vm.ShowOutlinePane);
    }
}
