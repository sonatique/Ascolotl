using Ascolotl.App.Views;
using Ascolotl.App.Views.Assist;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>The formatting toolbar and the editors' right-click menu.</summary>
public sealed class FormattingTests
{
    private static TextEditor SourceEditor(Window window) =>
        window.GetVisualDescendants().OfType<SourceEditorView>().Single()
            .GetVisualDescendants().OfType<TextEditor>().First();

    private static Button ToolbarButton(Window window, string name) =>
        window.GetVisualDescendants().OfType<FormatToolbar>().Single()
            .GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    private static void Click(Window window, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Pump(window);
    }

    private static List<string?> Headers(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();

    private static void Choose(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(i => i.Header as string == header)
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaFact]
    public void Toolbar_InSplitView_FormatsTheSourceSelection()
    {
        var (window, vm) = Open("Some words here.", EditorMode.Split);
        var editor = SourceEditor(window);
        editor.TextArea.Focus();
        editor.Select(5, 5);

        Click(window, ToolbarButton(window, Strings.FormatBold));

        Assert.Equal("Some *words* here.", vm.SelectedDocument!.SourceText);
        Assert.Equal("words", editor.SelectedText);
    }

    [AvaloniaFact]
    public void Toolbar_FollowsTheDocumentFormat()
    {
        var (window, vm) = Open("Some words here.", EditorMode.Split);
        vm.SelectedDocument!.FilePath = Path.Combine(Path.GetTempPath(), "notes.md");
        Pump(window);
        var editor = SourceEditor(window);
        editor.TextArea.Focus();
        editor.Select(5, 5);

        Click(window, ToolbarButton(window, Strings.FormatBold));

        Assert.Equal("Some **words** here.", vm.SelectedDocument.SourceText);
        Assert.False(ToolbarButton(window, Strings.InsertNote).IsVisible);
        Assert.True(ToolbarButton(window, Strings.InsertTable).IsVisible);
    }

    [AvaloniaFact]
    public void Toolbar_InWysiwyg_FormatsTheOpenBlockEditor_AndKeepsItOpen()
    {
        var (window, _) = Open("Hello world", EditorMode.Wysiwyg);
        var editor = OpenInlineEditor(window);
        editor.Select(6, 5);

        Click(window, ToolbarButton(window, Strings.FormatItalic));

        Assert.Same(editor, Preview(window).GetVisualDescendants().OfType<TextEditor>().Single());
        Assert.Equal("Hello _world_", editor.Text.TrimEnd());
    }

    [AvaloniaFact]
    public void Toolbar_InWysiwyg_WithNoBlockOpen_SaysWhereToClick()
    {
        var (window, vm) = Open("Hello world", EditorMode.Wysiwyg);

        Click(window, ToolbarButton(window, Strings.FormatBold));

        Assert.Equal(Strings.StatusClickTextToFormat, vm.StatusText);
        Assert.Equal("Hello world", vm.SelectedDocument!.SourceText);
    }

    [AvaloniaFact]
    public void Toolbar_AppliesTheInPlaceEdit_AndOffersItOnlyWhileOpen()
    {
        var (window, vm) = Open("Hello world", EditorMode.Wysiwyg);
        Assert.False(ToolbarButton(window, Strings.InlineApplyTip).IsEffectivelyVisible);

        var editor = OpenInlineEditor(window);
        Assert.Empty(Preview(window).GetVisualDescendants().OfType<Button>()); // nothing to clip in the block
        editor.Document.Insert(editor.Document.TextLength, " again");
        Click(window, ToolbarButton(window, Strings.InlineApplyTip));

        Assert.Equal("Hello world again", vm.SelectedDocument!.SourceText.TrimEnd());
        Assert.Empty(Preview(window).GetVisualDescendants().OfType<TextEditor>());
        Assert.False(ToolbarButton(window, Strings.InlineApplyTip).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Toolbar_CancelsTheInPlaceEdit()
    {
        var (window, vm) = Open("Hello world", EditorMode.Wysiwyg);
        var editor = OpenInlineEditor(window);
        editor.Document.Insert(editor.Document.TextLength, " again");

        Click(window, ToolbarButton(window, Strings.InlineCancelTip));

        Assert.Equal("Hello world", vm.SelectedDocument!.SourceText);
        Assert.Empty(Preview(window).GetVisualDescendants().OfType<TextEditor>());
        Assert.False(vm.SelectedDocument.IsDirty);
    }

    private const string Table = "Intro.\n\n|===\n| a | b\n| c | d\n|===\n";

    [AvaloniaFact]
    public void TableTools_ShowInATable_AndInsertARow()
    {
        var (window, vm) = Open(Table, EditorMode.Split);
        var editor = SourceEditor(window);
        editor.TextArea.Focus();
        editor.CaretOffset = 2; // in "Intro."
        Pump(window);
        Assert.False(ToolbarButton(window, Strings.TableRowBelow).IsEffectivelyVisible);

        editor.CaretOffset = Table.IndexOf("| a", StringComparison.Ordinal) + 2;
        Pump(window);
        Click(window, ToolbarButton(window, Strings.TableRowBelow));

        Assert.Equal("Intro.\n\n|===\n| a | b\n| |\n| c | d\n|===\n", vm.SelectedDocument!.SourceText);
        Assert.True(ToolbarButton(window, Strings.TableRowBelow).IsEffectivelyVisible); // the caret is in the new row
    }

    [AvaloniaFact]
    public void TableRightClick_OffersRowsAndColumns_AndMergeOnlyInAsciiDoc()
    {
        var (window, vm) = Open(Table, EditorMode.Split);
        var editor = SourceEditor(window);
        var assist = CompletionController.For(editor)!;
        editor.CaretOffset = Table.IndexOf("| a", StringComparison.Ordinal) + 2;

        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        Assert.Contains(Strings.MenuMerge, Headers(assist.Menu!));
        var column = assist.Menu!.Items.OfType<MenuItem>().Single(i => i.Header as string == Strings.MenuColumn);
        column.Items.OfType<MenuItem>().Single(i => i.Header as string == Strings.MenuInsertRight)
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("Intro.\n\n|===\n| a | | b\n| c | | d\n|===\n", vm.SelectedDocument!.SourceText);
        assist.Menu.Close();

        vm.SelectedDocument.FilePath = Path.Combine(Path.GetTempPath(), "t.md");
        editor.Document.Text = "| a | b |\n| --- | --- |\n| c | d |\n";
        editor.CaretOffset = 2;
        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        Assert.Contains(Strings.MenuRow, Headers(assist.Menu!));
        Assert.DoesNotContain(Strings.MenuMerge, Headers(assist.Menu!));
        assist.Menu.Close();
    }

    [AvaloniaFact]
    public void TableTools_InACellEditor_EditTheWholeTable()
    {
        var (window, vm) = Open("|===\n| Rev | Data\n\n| 1.0 | First\n|===\n", EditorMode.Wysiwyg);
        var cell = Preview(window).GetVisualDescendants().OfType<TextBlock>()
            .First(t => (t.Inlines is { Count: > 0 } i ? i.Text : t.Text) == "1.0");
        Click(window, cell);
        var editor = Preview(window).GetVisualDescendants().OfType<TextEditor>().Single();
        editor.Document.Insert(editor.Document.TextLength, "1"); // typed text is kept

        Click(window, ToolbarButton(window, Strings.TableColumnRight));

        Assert.Equal("|===\n| Rev | | Data\n\n| 1.01 | | First\n|===\n", vm.SelectedDocument!.SourceText);
        Assert.Empty(Preview(window).GetVisualDescendants().OfType<TextEditor>());
    }

    [AvaloniaFact]
    public void Toolbar_IsHiddenInTheSourceView()
    {
        var (window, vm) = Open("text", EditorMode.Split);
        Assert.True(window.GetVisualDescendants().OfType<FormatToolbar>().Single().IsEffectivelyVisible);

        vm.EditorMode = EditorMode.Source;
        Pump(window);
        Assert.False(window.GetVisualDescendants().OfType<FormatToolbar>().Single().IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void RightClick_OffersTheSelectionsFormatting_OrWhatToInsert()
    {
        var (window, _) = Open("Some words here.", EditorMode.Split);
        var editor = SourceEditor(window);
        var assist = CompletionController.For(editor)!;

        editor.Select(5, 5);
        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        var withSelection = Headers(assist.Menu!);
        Assert.Contains(Strings.MenuCut, withSelection);
        Assert.DoesNotContain(Strings.FormatInsertMenu, withSelection);
        assist.Menu!.Close();

        editor.Select(0, 0);
        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        var atCaret = Headers(assist.Menu!);
        Assert.DoesNotContain(Strings.MenuCut, atCaret);
        Assert.Contains(Strings.FormatInsertMenu, atCaret);
        assist.Menu!.Close();
    }

    [AvaloniaFact]
    public void RightClick_MovesTheCaretToTheClick_AndFormatsThere()
    {
        var (window, vm) = Open("Some words here.", EditorMode.Split);
        var editor = SourceEditor(window);
        var assist = CompletionController.For(editor)!;
        editor.TextArea.Focus();
        editor.CaretOffset = 0;
        Pump(window);

        var view = editor.TextArea.TextView;
        var visual = view.GetVisualPosition(new TextViewPosition(1, 17), AvaloniaEdit.Rendering.VisualYPosition.LineMiddle);
        var p = view.TranslatePoint(visual - view.ScrollOffset + new Vector(2, 0), window)!.Value;
        window.MouseDown(p, MouseButton.Right);
        window.MouseUp(p, MouseButton.Right);
        Pump(window);

        Assert.NotNull(assist.Menu);
        Assert.Equal(16, editor.CaretOffset);
        Choose(assist.Menu!, Strings.FormatCode);
        Pump(window);
        Assert.Equal("Some words here.``", vm.SelectedDocument!.SourceText);
    }

    [AvaloniaFact]
    public void RightClickMenu_InTheBlockEditor_KeepsTheEditOpen()
    {
        var (window, _) = Open("Hello world", EditorMode.Wysiwyg);
        var editor = OpenInlineEditor(window);
        var assist = CompletionController.For(editor)!;
        editor.Select(6, 5);

        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        assist.Menu!.Focus(); // the keyboard moves to the menu while it is open
        Pump(window);
        Assert.Single(Preview(window).GetVisualDescendants().OfType<TextEditor>());

        Choose(assist.Menu, Strings.FormatBold);
        Pump(window);
        Assert.Same(editor, Preview(window).GetVisualDescendants().OfType<TextEditor>().Single());
        Assert.Equal("Hello *world*", editor.Text.TrimEnd());
    }
}
