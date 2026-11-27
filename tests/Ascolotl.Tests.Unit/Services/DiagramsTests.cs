using AdocNet.Ast;
using AdocNet.Extensions;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.ViewModels;
using Ascolotl.Export;
using Ascolotl.Tests.Unit.Mocks;

namespace Ascolotl.Tests.Unit.Services;

// The diagram renderer is app-wide: these tests set it, so nothing else may render meanwhile.
[CollectionDefinition(nameof(DiagramsTests), DisableParallelization = true)]
public sealed class DiagramsCollection;

[Collection(nameof(DiagramsTests))]
public sealed class DiagramsTests : IDisposable
{
    private const string Mermaid = "= Doc\n\n[mermaid]\n----\ngraph TD; A-->B\n----\n";

    // A 1×1 PNG, as a diagram tool would write it.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly string _dir = Directory.CreateTempSubdirectory("ascolotl-diagrams-").FullName;
    private readonly PngRunner _runner = new();

    public DiagramsTests() => Diagrams.Use(new DiagramPass(_runner, _dir));

    public void Dispose()
    {
        Diagrams.Use(null);
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class PngRunner : IDiagramToolRunner
    {
        public int Calls;
        public bool IsAvailable => true;

        public string? Generate(string language, string source, string outputDirectory)
        {
            Interlocked.Increment(ref Calls);
            var path = Path.Combine(outputDirectory, Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, Png);
            return path;
        }
    }

    private static bool HasImage(DocumentNode document) => document.Children.OfType<BlockImageNode>().Any();

    [Fact]
    public void Off_IsTheDefault_AndRunsNothing()
    {
        Diagrams.Configure(new AppSettings().Diagrams);

        Assert.Null(Diagrams.Pass);
    }

    [Theory]
    [InlineData(DiagramMode.LocalTools)]
    [InlineData(DiagramMode.Kroki)]
    public void ChoosingAMode_SetsUpTheRenderer(DiagramMode mode)
    {
        Diagrams.Configure(mode, "https://kroki.example");

        Assert.NotNull(Diagrams.Pass);
    }

    [Fact]
    public void LocalTool_IsFoundOnThePath_OrIsMissing()
    {
        Assert.NotNull(Diagrams.LocalTool.Find("dotnet")); // on the PATH wherever the tests run
        Assert.Null(Diagrams.LocalTool.Find("no-such-diagram-tool-" + Guid.NewGuid().ToString("N")));
        Assert.False(new Diagrams.LocalTool("no-such-diagram-tool-" + Guid.NewGuid().ToString("N"), "").IsAvailable);
    }

    [Fact]
    public void LocalTool_RunsTheProgram_AndReturnsItsImage()
    {
        // A stand-in for dot: copies a PNG to the output path ({output} is its second argument).
        File.WriteAllBytes(Path.Combine(_dir, "diagram.png"), Png);
        var tool = Path.Combine(_dir, "fakedot");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(tool + ".cmd", "@copy /y \"%~dp0diagram.png\" %2 >nul\r\n");
        }
        else
        {
            File.WriteAllText(tool, "#!/bin/sh\ncp \"$(dirname \"$0\")/diagram.png\" \"$2\"\n");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var runner = new Diagrams.LocalTool(tool, "{input} {output}");

        var image = runner.Generate("graphviz", "digraph { a -> b }", Path.Combine(_dir, "out dir"));

        Assert.True(runner.IsAvailable);
        Assert.NotNull(image);
        Assert.Equal(Png, File.ReadAllBytes(image!));
    }

    [Fact]
    public async Task Preview_ShowsCodeFirst_ThenTheImageOnceDrawn()
    {
        var service = new AdocRenderService();

        var first = await service.RenderAsync(Mermaid, null, false, false, CancellationToken.None);
        Assert.Equal(1, first.PendingDiagrams);
        Assert.False(HasImage(first.Document!));

        await Diagrams.Pass!.GenerateAsync(first.Document!);
        var second = await service.RenderAsync(Mermaid, null, false, false, CancellationToken.None);

        Assert.Equal(0, second.PendingDiagrams);
        Assert.True(HasImage(second.Document!));
    }

    [Fact]
    public async Task ViewModel_DrawsPendingDiagrams_AndRendersAgain()
    {
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new AdocRenderService(),
            new MockExportService(), new MockRecoveryStore(), new MockSessionStore(), new MockAppLog(), debounceMs: 0);
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.SelectedDocument!.SourceText = Mermaid;
        await vm.PendingRenderTask;

        // Drawing runs in the background, then asks for a new render.
        for (var i = 0; i < 250 && !(vm.SelectedDocument.RenderedDocument is { } d && HasImage(d)); i++)
        {
            await Task.Delay(20);
            if (vm.PendingRenderTask is { } pending) await pending;
        }

        Assert.True(HasImage(vm.SelectedDocument.RenderedDocument!));
        Assert.Equal(1, _runner.Calls);
    }

    [Fact]
    public void ViewModel_DiagramMode_ConfiguresTheRenderer()
    {
        var vm = new MainWindowViewModel(new MockFileService(), new MockDialogService(), new MockRenderService());

        vm.SetDiagramModeCommand.Execute(DiagramMode.LocalTools);
        Assert.True(vm.IsDiagramsLocal);
        Assert.NotNull(Diagrams.Pass);

        vm.SetDiagramModeCommand.Execute(DiagramMode.Off);
        Assert.True(vm.IsDiagramsOff);
        Assert.Null(Diagrams.Pass);
    }

    [Theory]
    [InlineData(ExportFormat.Html, "src=\"data:image/png;base64,")]
    [InlineData(ExportFormat.DocBook, "fileref=\"")]
    public async Task Export_DrawsTheDiagrams(ExportFormat format, string expected)
    {
        var path = Path.Combine(_dir, "out." + format.Extension());

        var result = await new ExportService().ExportAsync(Mermaid, false, _dir, path, format, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var text = File.ReadAllText(path);
        Assert.Contains(expected, text);
        Assert.DoesNotContain("graph TD", text);
    }

    [Fact]
    public async Task DocBookExport_RefersToACopyBesideIt()
    {
        var output = Directory.CreateDirectory(Path.Combine(_dir, "out")).FullName;
        var path = Path.Combine(output, "book.xml");

        var result = await new ExportService().ExportAsync(Mermaid + "\nimage::photo.png[]\n", false, _dir, path, ExportFormat.DocBook, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var text = File.ReadAllText(path);
        var copy = System.Text.RegularExpressions.Regex.Match(text, "fileref=\"(book-diagrams/[^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(copy);
        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(output, copy)));
        Assert.DoesNotContain(_dir, text);
        Assert.Contains("fileref=\"photo.png\"", text); // the document's own image as written
    }

    [Fact]
    public async Task PdfExport_EmbedsTheDiagram()
    {
        var path = Path.Combine(_dir, "out.pdf");

        var result = await new ExportService().ExportAsync(Mermaid, false, _dir, path, ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task PdfExport_OfAnUnsavedDocument_EmbedsTheDiagram()
    {
        var path = Path.Combine(_dir, "unsaved.pdf");

        var result = await new ExportService().ExportAsync(Mermaid, false, null, path, ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)));
    }
}
