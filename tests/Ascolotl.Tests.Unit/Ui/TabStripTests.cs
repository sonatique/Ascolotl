using Ascolotl.App.Views;
using Ascolotl.Core.ViewModels;
using Ascolotl.Tests.Unit.Mocks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>Many open documents: the tab strip scrolls and offers a list of all tabs.</summary>
public sealed class TabStripTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) Open(int documents)
    {
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), new MockSessionStore(), new MockAppLog(), debounceMs: 0);
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 600 };
        window.Show();
        for (var i = 0; i < documents; i++)
            vm.NewDocumentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void MenuBarAndTabText_ShareALine()
    {
        var (window, vm) = Open(1);
        vm.HamburgerMenu = false;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        double Top(Visual v) => v.TranslatePoint(default, window)!.Value.Y;

        var file = window.GetVisualDescendants().OfType<MenuItem>()
            .First(m => m.Header as string == Ascolotl.Core.Resources.Strings.File);
        var menuText = file.GetVisualDescendants().OfType<TextBlock>().First(t => t.Bounds.Height > 0);
        var tab = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStripItem>().First();
        var tabText = tab.GetVisualDescendants().OfType<TextBlock>().First(t => t.Bounds.Height > 0);

        Assert.Equal(Top(tabText), Top(menuText), 0.5);
        Assert.Equal(9, file.Padding.Left); // the menu bar keeps its own padding, not the dropdown rows'
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void TabBackgrounds_StayOpaque_EvenMidFade(string theme)
    {
        // A tab's background fades between states. A see-through end colour
        // (Transparent is transparent white) makes the fade pass through grey.
        Avalonia.Application.Current!.RequestedThemeVariant =
            theme == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        try
        {
            var (window, vm) = Open(2);
            vm.SelectedDocument = vm.Documents[0]; // the second tab fades from selected to idle
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            foreach (var tab in window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStripItem>())
                Assert.Equal(255, ((Avalonia.Media.ISolidColorBrush)tab.Background!).Color.A);
        }
        finally
        {
            Avalonia.Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void FewTabs_NoAllTabsButton()
    {
        var (window, _) = Open(2);

        Assert.False(window.FindControl<Button>("AllTabsButton")!.IsVisible);
    }

    [AvaloniaFact]
    public void ManyTabs_ShowAllTabsButton_AndKeepSelectedTabInView()
    {
        var (window, vm) = Open(30);
        var scroll = window.FindControl<ScrollViewer>("TabScroll")!;

        Assert.True(window.FindControl<Button>("AllTabsButton")!.IsVisible);

        vm.SelectedDocument = vm.Documents[^1];
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.True(scroll.Offset.X > 0, "the last tab should have been scrolled into view");
    }

    [AvaloniaFact]
    public void StatusBar_ShowsFullPathOfCurrentFile()
    {
        var (window, vm) = Open(1);
        vm.SelectedDocument!.FilePath = @"C:\docs\guide\manual.adoc";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == @"C:\docs\guide\manual.adoc" && t.IsEffectivelyVisible);
    }
}
