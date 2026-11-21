using System.Reflection;
using System.Text.Json;
using Ascolotl.App.Views;
using Ascolotl.Core.Resources;
using Ascolotl.Tests.Unit.Mocks;
using Ascolotl.Core.ViewModels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;

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

    [AvaloniaFact]
    public void Notices_NameEveryPackageTheAppShips_AndOpenFromTheAboutBox()
    {
        var notices = AboutDialog.ThirdPartyNotices();
        var packages = AppPackages();
        Assert.Contains("SkiaSharp", packages); // the walk reached the native graphics stack
        var missing = packages.Where(id => !notices.Contains($"\n{id} ", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"Run tools/notices/make_notices.py: no notice for {string.Join(", ", missing)}");
        Assert.Contains("JetBrains Mono", notices); // fonts shipped inside packages and the app
        Assert.Contains("Inter typeface", notices);

        var dialog = new AboutDialog();
        dialog.Show();
        Assert.DoesNotContain("@", dialog.FindControl<TextBlock>("LegalText")!.Text); // a name, not an email
        dialog.FindControl<HyperlinkButton>("NoticesLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var viewer = Assert.Single(dialog.OwnedWindows);
        Assert.Contains("SkiaSharp", Assert.IsType<TextEditor>(viewer.Content).Text);
        viewer.Close();
        dialog.Close();
    }

    /// <summary>The packages Ascolotl.App needs at run time, from this test's deps.json.</summary>
    private static IEnumerable<string> AppPackages()
    {
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            Assembly.GetExecutingAssembly().GetName().Name + ".deps.json")));
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        var libraries = deps.RootElement.GetProperty("libraries");
        var seen = new HashSet<string>();
        var app = typeof(AboutDialog).Assembly.GetName().Name + "/";
        var queue = new Queue<string>([target.EnumerateObject().Single(p => p.Name.StartsWith(app, StringComparison.Ordinal)).Name]);
        while (queue.TryDequeue(out var key))
        {
            if (!seen.Add(key) || !target.TryGetProperty(key, out var node)) continue;
            if (node.TryGetProperty("dependencies", out var children))
                foreach (var child in children.EnumerateObject())
                    queue.Enqueue($"{child.Name}/{child.Value.GetString()}");
        }
        return seen.Where(k => libraries.TryGetProperty(k, out var lib) && lib.GetProperty("type").GetString() == "package")
            .Select(k => k[..k.IndexOf('/')]).ToList();
    }

    [AvaloniaFact]
    public void HelpMenu_LinksToIssues_AndTheSponsorPage()
    {
        var help = new AppMenus().GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == Strings.Help);
        var items = help.Items.OfType<MenuItem>().ToList();

        Assert.Equal("https://github.com/sonatique/Ascolotl/issues", AppLinks.Issues.ToString());
        Assert.Equal("https://buymeacoffee.com/sonatique", AppLinks.Sponsor.ToString());
        Assert.Contains(items, m => m.Header as string == Strings.MenuReportIssue && Equals(m.Tag, AppLinks.Issues));
        Assert.Contains(items, m => m.Header as string == Strings.AboutSponsorLink && Equals(m.Tag, AppLinks.Sponsor));
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
