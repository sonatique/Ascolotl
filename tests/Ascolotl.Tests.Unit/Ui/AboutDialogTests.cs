using Ascolotl.App.Views;
using Ascolotl.Core.Resources;
using Ascolotl.Tests.Unit.Mocks;
using Ascolotl.Core.ViewModels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class AboutDialogTests
{
    [Theory]
    [InlineData("0.0.1+d1b4336a94c29062a91ea18fd4bcc2940337c956", "0.0.1 (d1b4336)")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2.0+abc", "1.2.0 (abc)")]
    [InlineData(null, "?")]
    public void Version_ShowsTheCommitShort(string? informational, string label)
        => Assert.Equal(label, AboutDialog.VersionLabel(informational));

    [AvaloniaFact]
    public void Links_SourceAndSponsor_StoreOnlyOnceListed()
    {
        var dialog = new AboutDialog();
        dialog.Show();
        var links = dialog.GetLogicalDescendants().OfType<HyperlinkButton>().ToList();

        Assert.Contains(links, l => l.NavigateUri == AppLinks.Source && l.IsVisible);
        Assert.Equal(AppLinks.Store is not null, links.Single(l => l.Name == "StoreLink").IsVisible);
        Assert.Contains(dialog.GetLogicalDescendants().OfType<Button>(), b => b.Content as string == Strings.AboutSponsorLink);
        // GPL: an interactive program shows its copyright, licence and lack of warranty.
        Assert.Contains(Strings.AboutLicenseNotice,
            dialog.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "LegalText").Text);
        dialog.Close();
    }

    [Fact]
    public void AboutCommand_OpensTheDialog_AndIsInThePalette()
    {
        var dialogs = new MockDialogService();
        var vm = new MainWindowViewModel(new MockFileService(), dialogs, new MockRenderService(),
            new MockExportService(), new MockRecoveryStore(), new MockSessionStore(), new MockAppLog(), 0);

        vm.ShowAboutCommand.Execute(null);

        Assert.Equal(1, dialogs.ShowAboutCalled);
        Assert.Contains(vm.CommandPalette.FilteredCommands, c => c.Name == Strings.CommandAbout);
    }
}
