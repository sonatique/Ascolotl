using Ascolotl.App.Views;
using Ascolotl.Core.ViewModels;
using Ascolotl.Tests.Unit.Mocks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: AvaloniaTestApplication(typeof(Ascolotl.Tests.Unit.Ui.HeadlessApp))]

namespace Ascolotl.Tests.Unit.Ui;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Ascolotl.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Switching between the hamburger menu and the menu bar (View > Hamburger Menu)
/// must leave every menu opening with all its items: it used to crash or show
/// an empty menu.
/// </summary>
public sealed class MenuModeTests
{
    private static readonly string[] Menus = ["_File", "_Import", "_Export", "_View", "_Style"];

    private static (MainWindow Window, MainWindowViewModel Vm) Open(bool hamburger)
    {
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), new MockSessionStore(), new MockAppLog(), debounceMs: 0)
        { HamburgerMenu = hamburger };
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static Menu MainMenu(MainWindow window) => window.FindControl<Menu>("MainMenu")!;
    private static MenuItem Hamburger(MainWindow window) => window.FindControl<MenuItem>("HamburgerMenuItem")!;

    /// <summary>The visible menu with this header: in the hamburger or in the bar.</summary>
    private static MenuItem Find(MainWindow window, string header)
    {
        var hamburger = Hamburger(window);
        var candidates = hamburger.IsVisible
            ? hamburger.Items.OfType<MenuItem>()
            : MainMenu(window).Items.OfType<MenuItem>().Where(m => m.IsVisible && m != hamburger);
        return candidates.Single(m => (string?)m.Header == header);
    }

    /// <summary>Opens a menu as a user does and checks every item is on screen.</summary>
    private static void OpenAndCheck(MainWindow window, string header)
    {
        if (Hamburger(window).IsVisible)
            Hamburger(window).Open();
        var menu = Find(window, header);
        menu.Open();
        Dispatcher.UIThread.RunJobs();

        var items = menu.Items.OfType<MenuItem>().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.NotNull(item.GetVisualRoot()));
        MainMenu(window).Close();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Clicks View > Hamburger Menu with the View menu open, as a user does.</summary>
    private static void ToggleFromViewMenu(MainWindow window, MainWindowViewModel vm)
    {
        if (Hamburger(window).IsVisible)
            Hamburger(window).Open();
        Find(window, "_View").Open();
        Dispatcher.UIThread.RunJobs();
        vm.ToggleHamburgerMenuCommand.Execute(null);
        MainMenu(window).Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SwitchToMenuBar_ThenOpenFile_Works()
    {
        var (window, vm) = Open(hamburger: true);

        ToggleFromViewMenu(window, vm);

        Assert.False(Hamburger(window).IsVisible);
        OpenAndCheck(window, "_File");
        OpenAndCheck(window, "_View");
    }

    [AvaloniaFact]
    public void SwitchingBackAndForth_KeepsEveryMenuUsable()
    {
        var (window, vm) = Open(hamburger: true);

        for (var round = 0; round < 3; round++)
        {
            foreach (var header in Menus)
                OpenAndCheck(window, header);
            ToggleFromViewMenu(window, vm); // to the menu bar
            foreach (var header in Menus)
                OpenAndCheck(window, header);
            ToggleFromViewMenu(window, vm); // back to the hamburger
        }
        Assert.True(Hamburger(window).IsVisible);
    }

    [AvaloniaFact]
    public void RecentFiles_ListedInBothMenuSets()
    {
        var (window, vm) = Open(hamburger: true);
        vm.LoadRecentFiles(["C:/docs/a.adoc", "C:/docs/b.adoc"]);
        Dispatcher.UIThread.RunJobs();

        foreach (var set in new[] { true, false })
        {
            if (Hamburger(window).IsVisible != set)
                ToggleFromViewMenu(window, vm);
            var recent = Find(window, "_File").Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Recent _Files");
            Assert.Equal(["C:/docs/a.adoc", "C:/docs/b.adoc"], recent.Items.OfType<MenuItem>().Select(m => (string?)m.Header));
        }
    }
}
