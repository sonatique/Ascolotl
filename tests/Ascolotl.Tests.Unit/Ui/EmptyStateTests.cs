using Ascolotl.App.Views;
using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;
using Ascolotl.Tests.Unit.Mocks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class EmptyStateTests
{
    [AvaloniaFact]
    public void ImportChip_OffersEveryFormat_AndRunsTheImportCommand()
    {
        var files = new MockFileService();
        var vm = new MainWindowViewModel(files, new MockDialogService(), new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), new MockSessionStore(), new MockAppLog(), 0,
            importService: new Ascolotl.Export.DocumentImportService());
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var chip = window.GetVisualDescendants().OfType<EmptyStateView>().Single()
            .GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Import…");
        var flyout = Assert.IsType<MenuFlyout>(chip.Flyout);
        flyout.ShowAt(chip);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.OfType<MenuItem>().ToList();

        Assert.Equal(Enum.GetValues<ImportFormat>().Order(), items.Select(i => (ImportFormat)i.CommandParameter!).Order());
        var markdown = items.Single(i => (ImportFormat)i.CommandParameter! == ImportFormat.Markdown);
        Assert.NotNull(markdown.Command);
        markdown.Command!.Execute(markdown.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, files.PickOpenFilePathCalled);
    }
}
