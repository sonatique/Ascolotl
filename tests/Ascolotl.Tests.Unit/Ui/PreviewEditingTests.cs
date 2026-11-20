using Ascolotl.App.Views;
using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;
using Ascolotl.Tests.Unit.Mocks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>In-place preview editing and external edits against the source editor.</summary>
public sealed class PreviewEditingTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string text, EditorMode mode, int debounceMs = 0,
        MockFileService? files = null)
    {
        var vm = new MainWindowViewModel(files ?? new MockFileService(), new MockDialogService(), new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), new MockSessionStore(), new MockAppLog(), debounceMs)
        { EditorMode = mode };
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = text;
        vm.RequestRender(immediate: true);
        Pump(window);
        vm.SelectedDocument.MarkClean();
        return (window, vm);
    }

    private static void Pump(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static PreviewView Preview(Window window) =>
        window.GetVisualDescendants().OfType<PreviewView>().Single();

    /// <summary>Window point at the centre of the first rendered text in the preview.</summary>
    private static Point FirstTextPoint(Window window)
    {
        var tb = Preview(window).GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.IsEffectivelyVisible && t.Bounds.Width > 0);
        return tb.TranslatePoint(new Point(4, tb.Bounds.Height / 2), window)!.Value;
    }

    private static TextEditor OpenInlineEditor(Window window)
    {
        var p = FirstTextPoint(window);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Pump(window);
        return Preview(window).GetVisualDescendants().OfType<TextEditor>().Single();
    }

    [AvaloniaFact]
    public async Task Save_WithOpenInlineEdit_WritesTheTypedText()
    {
        var files = new MockFileService { SaveFileAsResult = Path.Combine(Path.GetTempPath(), "a.adoc") };
        var (window, vm) = Open("Hello world", EditorMode.Wysiwyg, files: files);
        var editor = OpenInlineEditor(window);

        editor.Document.Insert(editor.Document.TextLength, " again");
        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal("Hello world again", files.LastSavedContent?.TrimEnd());
    }

    [AvaloniaFact]
    public void CancelledInlineEdit_LeavesDocumentClean()
    {
        var (window, vm) = Open("Hello world", EditorMode.Wysiwyg);
        var editor = OpenInlineEditor(window);

        editor.Document.Insert(editor.Document.TextLength, " again");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump(window);

        Assert.False(vm.SelectedDocument!.IsDirty);
    }

    [AvaloniaFact]
    public void OpenInlineEdit_SurvivesRerender_MarksDirty_AndCommitsOnTabSwitch()
    {
        var (window, vm) = Open("Hello world", EditorMode.Wysiwyg);
        var doc = vm.SelectedDocument!;

        var p = FirstTextPoint(window);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Pump(window);
        var editor = Preview(window).GetVisualDescendants().OfType<TextEditor>().Single();

        editor.Document.Insert(editor.Document.TextLength, " again");
        Assert.True(doc.IsDirty, "typing in the inline editor must make closing prompt");

        // A re-render (word wrap toggle) must not throw the open edit away.
        vm.PreviewWordWrap = !vm.PreviewWordWrap;
        Pump(window);
        Assert.Contains(Preview(window).GetVisualDescendants().OfType<TextEditor>(), e => e.Text.EndsWith(" again"));

        // Switching documents commits the edit into the outgoing document.
        vm.NewDocumentCommand.Execute(null);
        Pump(window);
        Assert.Equal("Hello world again", doc.SourceText.TrimEnd());
    }

    [AvaloniaFact]
    public void BlockCommands_RefuseWhileTheRenderIsBehindTheText()
    {
        var (window, vm) = Open("First paragraph.", EditorMode.Split, debounceMs: 60_000);
        vm.SelectedDocument!.SourceText = "Other text.\n\nFirst paragraph.";
        Pump(window); // the debounced render has not run: the block map is stale

        var p = FirstTextPoint(window);
        window.MouseDown(p, MouseButton.Right);
        window.MouseUp(p, MouseButton.Right);

        Assert.Equal("The preview is updating — try again", vm.StatusText);
    }

    [AvaloniaFact]
    public void ExternalEdit_KeepsSourceEditorUndoAndCaret()
    {
        var (window, vm) = Open("alpha beta gamma", EditorMode.Split);
        var editor = window.GetVisualDescendants().OfType<SourceEditorView>().Single()
            .GetVisualDescendants().OfType<TextEditor>().First();

        editor.Document.Insert(0, "x");          // a user edit to undo later
        editor.CaretOffset = 3;
        vm.SelectedDocument!.SourceText = "xalpha beta GAMMA"; // e.g. a preview commit
        Pump(window);

        Assert.Equal("xalpha beta GAMMA", editor.Text);
        Assert.Equal(3, editor.CaretOffset);
        Assert.True(editor.Document.UndoStack.CanUndo);
    }

    [AvaloniaFact]
    public void SavedAsMarkdown_TheAsciiDocAssistStops()
    {
        var (window, vm) = Open("== Section\n\n* item", EditorMode.Split);
        var doc = vm.SelectedDocument!;
        var editor = window.GetVisualDescendants().OfType<SourceEditorView>().Single()
            .GetVisualDescendants().OfType<TextEditor>().First();
        editor.CaretOffset = 3;
        Assert.NotEmpty(doc.SyntaxHint);

        doc.FilePath = Path.Combine(Path.GetTempPath(), "notes.md");
        Assert.Empty(doc.SyntaxHint);

        // Enter after a list item no longer continues the list AsciiDoc's way.
        editor.CaretOffset = editor.Document.TextLength;
        editor.TextArea.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump(window);
        Assert.Equal("== Section\n\n* item\n", editor.Text.Replace("\r\n", "\n"));
        Assert.Empty(doc.SyntaxHint);
    }

    /// <summary>The rendered paragraph showing exactly <paramref name="text"/>.</summary>
    private static TextBlock Paragraph(Window window, string text) =>
        Preview(window).GetVisualDescendants().OfType<TextBlock>()
            .Single(t => (t.Inlines is { Count: > 0 } inlines ? inlines.Text : t.Text) == text);

    [AvaloniaFact]
    public void Rerender_KeepsTheVisualsOfUnchangedBlocks()
    {
        var (window, vm) = Open("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.", EditorMode.Split);
        var first = Paragraph(window, "First paragraph.");
        var third = Paragraph(window, "Third paragraph.");

        vm.SelectedDocument!.SourceText = "First paragraph.\n\nSecond paragraph, edited.\n\nThird paragraph.";
        Pump(window);

        Assert.Same(first, Paragraph(window, "First paragraph."));
        Assert.Same(third, Paragraph(window, "Third paragraph."));
        Paragraph(window, "Second paragraph, edited.");
    }

    [AvaloniaFact]
    public void KeptBlockBelowAddedLines_MapsItsTextToTheNewSourceLine()
    {
        // Formatting gives the paragraph inline runs, which carry source lines
        // for click-to-source.
        var (window, vm) = Open("First paragraph.\n\nSecond *bold* paragraph.", EditorMode.Split);
        var second = Paragraph(window, "Second bold paragraph.");

        vm.SelectedDocument!.SourceText = "Added paragraph.\n\nFirst paragraph.\n\nSecond *bold* paragraph.";
        Pump(window);
        Assert.Same(second, Paragraph(window, "Second bold paragraph."));

        var bold = second.Inlines!.OfType<Avalonia.Controls.Documents.Bold>().Single();
        var range = Preview(window).CurrentRange(second, AdocNet.Avalonia.AvaloniaRenderer.GetSourceRange(bold));
        Assert.Equal(5, range.Start.Line); // 1-based, two lines lower than when it was rendered
        Assert.Equal(8, range.Start.Column);
    }

    [AvaloniaFact]
    public void CommittedBlockEdit_RebuildsItsBlockOnly()
    {
        var (window, vm) = Open("* one\n* two\n\nAfter the list.", EditorMode.Wysiwyg);
        var after = Paragraph(window, "After the list.");
        var editor = OpenInlineEditor(window);

        // A trailing space lays the list out exactly as before: the re-render
        // must still replace the frozen inline editor.
        editor.Document.Insert("* one".Length, " ");
        vm.FlushPendingEdits!();
        Pump(window);

        Assert.Empty(Preview(window).GetVisualDescendants().OfType<TextEditor>());
        Assert.Same(after, Paragraph(window, "After the list."));
    }

    [AvaloniaFact]
    public void ShowingTheTitle_AddsItsLine()
    {
        var (window, vm) = Open("= Document Title\n\nBody text.", EditorMode.Split);
        bool TitleShown() => Preview(window).GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Document Title");
        Assert.False(TitleShown());

        vm.ShowTitleAndToc = true; // re-renders; the body block is unchanged
        Pump(window);

        Assert.True(TitleShown());
    }
}
