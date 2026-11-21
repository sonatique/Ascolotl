using Ascolotl.App.Views;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Ascolotl.Core.ViewModels;
using Ascolotl.Tests.Unit.Mocks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>The next start brings back the tab in front and where each tab was scrolled.</summary>
public sealed class SessionRestoreTests : IDisposable
{
    // Section k's heading is on 0-based line 2 + 4 * (k - 1).
    private static readonly string Text =
        "= Doc\n\n" + string.Concat(Enumerable.Range(1, 200).Select(i => $"== Section {i}\n\nSome words.\n\n"));

    private const int Section16 = 62;
    private const int Section40 = 158;

    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var f in _files) File.Delete(f);
    }

    private string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ascolotl-session-{Guid.NewGuid():N}.adoc");
        File.WriteAllText(path, Text);
        _files.Add(path);
        return path;
    }

    private async Task<(MainWindow Window, MainWindowViewModel Vm)> Restore(EditorMode mode, int frontTopLine,
        double frontPageScroll = 0, bool pagePreview = false)
    {
        var sessions = new MockSessionStore
        {
            NextLoadResult = new SessionState
            {
                SelectedIndex = 1,
                OpenDocuments =
                [
                    new() { DocumentId = "a", FilePath = TempFile(), DisplayName = "a.adoc", EditorMode = mode },
                    new() { DocumentId = "b", FilePath = TempFile(), DisplayName = "b.adoc", TopLine = frontTopLine, EditorMode = mode, PageScroll = frontPageScroll },
                ],
            },
        };
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), sessions, new MockAppLog(), debounceMs: 0)
        { PagePreview = pagePreview };
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        await vm.RestoreSessionAsync();
        Pump(window);
        return (window, vm);
    }

    private static int EditorTopLine(Window window)
    {
        var view = window.GetVisualDescendants().OfType<TextEditor>().First(e => e.IsEffectivelyVisible).TextArea.TextView;
        return view.GetDocumentLineByVisualTop(view.ScrollOffset.Y).LineNumber - 1;
    }

    [AvaloniaFact]
    public async Task Split_BringsBackTheFrontTab_AndItsPosition()
    {
        var (window, vm) = await Restore(EditorMode.Split, Section16);

        Assert.Same(vm.Documents[1], vm.SelectedDocument); // not the first tab the tab strip picks
        Assert.Equal(Section16, EditorTopLine(window));
        // The caret comes along, so the status-bar hint speaks of what shows, not of line 1.
        Assert.Equal(Section16 + 1, vm.SelectedDocument!.CursorLine);
        Assert.Equal(string.Format(Strings.HintSectionHeading, 1), vm.SelectedDocument.SyntaxHint);
    }

    [AvaloniaFact]
    public async Task Wysiwyg_BringsBackThePagePosition()
    {
        var (window, _) = await Restore(EditorMode.Wysiwyg, Section16);

        var scroll = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;
        var heading = Preview(window).GetVisualDescendants().OfType<TextBlock>()
            .First(t => (t.Inlines is { Count: > 0 } i ? i.Text : t.Text) == "Section 16");
        Assert.InRange(heading.TranslatePoint(default, scroll)!.Value.Y, -1, 40);
    }

    [AvaloniaFact]
    public async Task SwitchingTabs_KeepsEachTabsPosition()
    {
        var (window, vm) = await Restore(EditorMode.Split, 0);
        var editor = window.GetVisualDescendants().OfType<TextEditor>().First(e => e.IsEffectivelyVisible);
        var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroll.Offset = new Vector(0, editor.TextArea.TextView.GetVisualTopByDocumentLine(Section16 + 1));
        Pump(window);

        vm.SelectedDocument = vm.Documents[0];
        Pump(window);
        Assert.Equal(0, EditorTopLine(window));
        vm.SelectedDocument = vm.Documents[1];
        Pump(window);

        Assert.Equal(Section16, EditorTopLine(window));
        Assert.Equal(Section16, vm.Documents[1].TopLine);
    }

    [AvaloniaFact]
    public async Task LeavingWysiwyg_TheSourceShowsWhereThePageWas()
    {
        var (window, vm) = await Restore(EditorMode.Wysiwyg, Section16);
        var scroll = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;
        var heading = Preview(window).GetVisualDescendants().OfType<TextBlock>()
            .First(t => (t.Inlines is { Count: > 0 } i ? i.Text : t.Text) == "Section 40");
        scroll.Offset = new Vector(0, scroll.Offset.Y + heading.TranslatePoint(default, scroll)!.Value.Y); // the reader scrolls on
        Pump(window);

        vm.EditorMode = EditorMode.Split;
        Pump(window);

        Assert.InRange(EditorTopLine(window), Section40 - 1, Section40 + 1);
    }

    [AvaloniaFact]
    public async Task PagePreview_BringsBackThePagePosition_AndKeepsTheOneScrolledTo()
    {
        var (window, vm) = await Restore(EditorMode.Wysiwyg, 0, frontPageScroll: 0.5, pagePreview: true);
        var scroll = Preview(window).FindControl<ScrollViewer>("PreviewScroll")!;
        double Fraction() => scroll.Offset.Y / scroll.Extent.Height;
        // The pages are laid out off the UI thread.
        for (var i = 0; i < 200 && !(scroll.Extent.Height > 2000 && Fraction() > 0.4); i++)
        {
            await Task.Delay(20);
            Pump(window);
        }
        Assert.InRange(Fraction(), 0.49, 0.51);

        scroll.Offset = new Vector(0, scroll.Extent.Height / 4); // the reader scrolls up
        Pump(window);
        vm.SaveSession();

        Assert.InRange(vm.Documents[1].PageScroll, 0.24, 0.26);
    }

    [AvaloniaFact]
    public async Task EachTab_ComesBackInItsOwnView_AtItsPosition()
    {
        var (window, vm) = await Restore(EditorMode.Split, Section16);
        vm.Documents[0].EditorMode = EditorMode.Wysiwyg;

        vm.SelectedDocument = vm.Documents[0];
        Pump(window);
        Assert.Equal(EditorMode.Wysiwyg, vm.EditorMode);
        vm.SelectedDocument = vm.Documents[1];
        Pump(window);

        Assert.Equal(EditorMode.Split, vm.EditorMode);
        Assert.Equal(Section16, EditorTopLine(window));
    }
}
