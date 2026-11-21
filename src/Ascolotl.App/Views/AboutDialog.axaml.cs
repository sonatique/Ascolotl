using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ascolotl.Core.Resources;

namespace Ascolotl.App.Views;

/// <summary>Where Ascolotl lives on the web. A null address hides its link.</summary>
public static class AppLinks
{
    public static readonly Uri Source = new("https://github.com/sonatique/Ascolotl");
    public static readonly Uri License = new("https://github.com/sonatique/Ascolotl/blob/main/LICENSE");
    public static readonly Uri Sponsor = new("https://github.com/sponsors/sonatique");
    public static readonly Uri Issues = new("https://github.com/sonatique/Ascolotl/issues");

    /// <summary>The Store listing: https://apps.microsoft.com/detail/ followed by the
    /// product ID from Partner Center (Product management &gt; Product identity).</summary>
    public static readonly Uri? Store = null;
}

/// <summary>Name, version, links, the sponsor page, and the GPL's legal notice.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var assembly = typeof(AboutDialog).Assembly;
        VersionText.Text = string.Format(Strings.AboutVersion,
            VersionLabel(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion));
        LegalText.Text = $"{assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright}\n{Strings.AboutLicenseNotice}";
        StoreLink.NavigateUri = AppLinks.Store;
        StoreLink.IsVisible = AppLinks.Store is not null;
    }

    /// <summary>"0.0.1+&lt;commit&gt;" (the build appends the commit) as "0.0.1 (abc1234)".</summary>
    internal static string VersionLabel(string? informational)
    {
        if (string.IsNullOrEmpty(informational)) return "?";
        var plus = informational.IndexOf('+');
        if (plus < 0) return informational;
        var commit = informational[(plus + 1)..];
        return $"{informational[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    private async void OnSponsorClick(object? sender, RoutedEventArgs e)
        => await Launcher.LaunchUriAsync(AppLinks.Sponsor);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
