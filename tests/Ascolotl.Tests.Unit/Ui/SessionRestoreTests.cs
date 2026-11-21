using Ascolotl.App.Views;
using Ascolotl.Core.Models;
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

    private async Task<(MainWindow Window, MainWindowViewModel Vm)> Restore(EditorMode mode, int frontTopLine)
    {
        var sessions = new MockSessionStore
        {
            NextLoadResult = new SessionState
            {
                SelectedIndex = 1,
                OpenDocuments =
                [
                    new() { DocumentId = "a", FilePath = TempFile(), DisplayName = "a.adoc" },
                    new() { DocumentId = "b", FilePath = TempFile(), DisplayName = "b.adoc", TopLine = frontTopLine },
                ],
            },
        };
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), sessions, new MockAppLog(), debounceMs: 0)
        { EditorMode = mode };
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
}
