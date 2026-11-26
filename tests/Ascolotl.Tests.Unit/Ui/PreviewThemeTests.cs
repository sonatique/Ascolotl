using Ascolotl.App.Views.Assist;
using Ascolotl.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class PreviewThemeTests
{
    /// <summary>The page's text takes the theme's colour, light on the dark page,
    /// so notes, code blocks and table headers must be dark boxes there.</summary>
    [AvaloniaFact]
    public void DarkTheme_DarkensTheBoxesUnderThePagesText()
    {
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            var (window, vm) = Open("NOTE: noted\n\n----\nsome code\n----\n\n|===\n| Head\n\n| cell\n|===\n", EditorMode.Wysiwyg);
            foreach (var text in new[] { "NOTE", "some code", "Head" })
                Assert.True(Luminance(BoxUnder(window, text)) > 0.85, $"{text}: light box on the light page");

            app.RequestedThemeVariant = ThemeVariant.Dark; // the page re-renders with the dark look
            Pump(window);
            foreach (var text in new[] { "NOTE", "some code", "Head" })
                Assert.True(Luminance(BoxUnder(window, text)) < 0.25, $"{text}: dark box on the dark page");

            // Page Preview's pages are paper: light boxes and black text, also in the dark theme.
            vm.PagePreview = true;
            for (var i = 0; i < 250 && !Paper(window); i++) // the pages laid out, off the UI thread
            {
                Thread.Sleep(20);
                Pump(window);
            }
            Assert.True(Luminance(BoxUnder(window, "NOTE")) > 0.85, "NOTE: light box on paper");
            var noted = Preview(window).GetVisualDescendants().OfType<TextBlock>().First(t => PageSpelling.ShownText(t).Trim() == "noted");
            Assert.True(Luminance(((ISolidColorBrush)noted.Foreground!).Color) < 0.2, "black text on paper");
        }
        finally
        {
            app.RequestedThemeVariant = before;
        }
    }

    private static Color BoxUnder(Window window, string text)
    {
        var shown = Preview(window).GetVisualDescendants().OfType<TextBlock>().First(t => PageSpelling.ShownText(t).Trim() == text);
        return shown.GetVisualAncestors().OfType<Border>().Select(b => b.Background).OfType<ISolidColorBrush>().First().Color;
    }

    private static bool Paper(Window window) =>
        Preview(window).GetVisualDescendants().OfType<Border>().Any(b => b.Background is ISolidColorBrush { Color: var c } && c == Colors.White);

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
}
