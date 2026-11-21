using System.Text.RegularExpressions;
using Ascolotl.Core.Models;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.VisualTree;
using AvaloniaEdit;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class EditorFontTests
{
    [AvaloniaFact]
    public void SourceEditor_UsesTheBundledJetBrainsMono()
    {
        var (window, _) = Open("= Doc\n", EditorMode.Source);
        var family = window.GetVisualDescendants().OfType<TextEditor>().First().FontFamily;

        Assert.Equal("JetBrains Mono", family.FamilyNames[0]);
        // A wrong assembly name in the URI silently falls back to the next font.
        var file = new Uri(Regex.Match(family.Key!.Source.OriginalString, "avares://[^#,]+").Value);
        Assert.True(AssetLoader.Exists(file), $"{file} is not one of the app's resources");
    }
}
