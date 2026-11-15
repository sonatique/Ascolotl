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
    private static (MainWindow Window, MainWindowViewModel Vm) Open(string text, EditorMode mode, int debounceMs = 0)
    {
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new MockRenderService(),
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
}
