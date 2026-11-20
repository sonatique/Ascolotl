using Ascolotl.Core.Models;
using Ascolotl.Export;
using AdocNet.Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using static Ascolotl.Tests.Unit.Ui.PreviewEditingTests;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>Markdown files go through AdocNet's Markdown parser, in the preview and the exports.</summary>
public sealed class MarkdownPreviewTests
{
    private static TextBlock Paragraph(Window window) =>
        Preview(window).GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Inlines is { Count: > 0 } inlines && inlines.Text == "Some word here.");

    [AvaloniaFact]
    public void SavingAsMarkdown_RerendersWithMarkdownMeaning_AndMapsToItsLines()
    {
        // *word* is bold in AsciiDoc and italic in Markdown.
        var (window, vm) = Open("First.\n\nSome *word* here.", EditorMode.Split);
        Assert.Single(Paragraph(window).Inlines!.OfType<Bold>());

        vm.SelectedDocument!.FilePath = Path.Combine(Path.GetTempPath(), "notes.md");
        Pump(window);

        var italic = Assert.Single(Paragraph(window).Inlines!.OfType<Italic>());
        var range = AvaloniaRenderer.GetSourceRange(italic);
        Assert.Equal((3, 6), (range.Start.Line, range.Start.Column)); // 1-based, in the .md text
    }

    [AvaloniaFact]
    public void MarkdownBlock_OpensInItsInPlaceEditor_WithItsSource()
    {
        var (window, vm) = Open("Some *word* here.", EditorMode.Wysiwyg);
        vm.SelectedDocument!.FilePath = Path.Combine(Path.GetTempPath(), "notes.md");
        Pump(window);

        var editor = OpenInlineEditor(window);

        Assert.Equal("Some *word* here.", editor.Text.TrimEnd());
    }

    [Fact]
    public async Task MarkdownExport_UsesTheMarkdownParser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ascolotl-md-{Guid.NewGuid():N}.html");
        try
        {
            var result = await new ExportService().ExportAsync("# Notes\n\nSome *word* and ~~gone~~.\n", true, null, path,
                ExportFormat.Html, ExportTheme.Asciidoctor, null, null, CancellationToken.None);
            Assert.True(result.IsSuccess, result.ErrorMessage);
            var html = await File.ReadAllTextAsync(path);
            Assert.Contains("<em>word</em>", html);
            Assert.DoesNotContain("*word*", html);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Website_RendersMarkdownPages_AndLinksToThem()
    {
        var root = Directory.CreateTempSubdirectory("ascolotl-md-site-").FullName;
        var output = Directory.CreateTempSubdirectory("ascolotl-md-out-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "notes.md"), "# Notes\n\nSome *word*.\n");
            File.WriteAllText(Path.Combine(root, "guide.adoc"), "= Guide\n\nSee link:notes.md[the notes].\n");

            var result = await new WebsiteExportService().ExportAsync(root, ["guide.adoc", "notes.md"], output, CancellationToken.None);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Contains("<em>word</em>", File.ReadAllText(Path.Combine(output, "notes.html")));
            Assert.Contains("notes.html", File.ReadAllText(Path.Combine(output, "guide.html")));
            Assert.False(File.Exists(Path.Combine(output, "notes.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(output, recursive: true);
        }
    }
}
