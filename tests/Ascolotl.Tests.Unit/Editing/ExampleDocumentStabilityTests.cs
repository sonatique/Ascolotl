using Ascolotl.Core.Models;
using Ascolotl.Export;

namespace Ascolotl.Tests.Unit.Editing;

/// <summary>
/// Stability tests that load every .adoc file from example-documents/ and
/// push each one through the AdocNet render service and the PDF export
/// service. No assertion on exact output — these verify that no exceptions
/// are thrown and that basic structural invariants hold.
/// </summary>
public sealed class ExampleDocumentStabilityTests
{
    private static readonly string ExampleDocumentsPath =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "example-documents"));

    public static IEnumerable<object[]> AllExampleFiles()
    {
        if (!Directory.Exists(ExampleDocumentsPath))
            yield break;

        foreach (var file in Directory.GetFiles(ExampleDocumentsPath, "*.adoc"))
            yield return [Path.GetFileName(file), file];
    }

    // ------------------------------------------------------------------
    // Include resolution: no unresolved directives after IncludeResolver
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllExampleFiles))]
    public async Task AdocRenderService_RendersWithoutError(string fileName, string filePath)
    {
        _ = fileName;
        var source = File.ReadAllText(filePath);
        var baseDir = Path.GetDirectoryName(filePath)!;

        var service = new Ascolotl.Core.Services.AdocRenderService();
        var result = await service.RenderAsync(source, baseDir, markdown: false, standalone: true, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Render failed for {fileName}: {result.ErrorSummary}");
        Assert.NotNull(result.Document);
    }

    // ------------------------------------------------------------------
    // Renders pull in include::copyright.adoc[] content — verify the
    // included text reaches the parsed document for every doc that references it.
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllExampleFiles))]
    public async Task ParsedDocument_ResolvesIncludes(string fileName, string filePath)
    {
        _ = fileName;
        var source = File.ReadAllText(filePath);
        if (!source.Contains("include::"))
            return; // nothing to verify for docs without includes

        var baseDir = Path.GetDirectoryName(filePath)!;

        var service = new Ascolotl.Core.Services.AdocRenderService();
        var result = await service.RenderAsync(source, baseDir, markdown: false, standalone: true, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Render failed for {fileName}: {result.ErrorSummary}");
        // Every include resolved: no include diagnostic, and included lines are in the document.
        Assert.DoesNotContain(result.Diagnostics!, d => d.Message.Contains("include", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.LineOrigins!, o => o.IsSynthetic);
    }

    // ------------------------------------------------------------------
    // PDF export stability (AdocNet pure-managed AsciiDoc → PDF)
    // ------------------------------------------------------------------

    private const string SimpleAdoc = """
        = Test

        Hello world.
        """;

    [Fact]
    public async Task PdfExport_ProducesFile()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"ascolotl-test-{Guid.NewGuid():N}.pdf");

        try
        {
            var service = new ExportService();
            var result = await service.ExportAsync(SimpleAdoc, false, null, outputPath, ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

            Assert.True(result.IsSuccess, $"PDF export failed: {result.ErrorMessage}");
            Assert.True(File.Exists(outputPath), "PDF file was not created");
            Assert.True(new FileInfo(outputPath).Length > 0, "PDF file is empty");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PdfExport_WithBasePath_ProducesFile()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"ascolotl-test-{Guid.NewGuid():N}.pdf");

        try
        {
            var service = new ExportService();
            var result = await service.ExportAsync(SimpleAdoc, false, ExampleDocumentsPath, outputPath, ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

            Assert.True(result.IsSuccess, $"PDF export with basePath failed: {result.ErrorMessage}");
            Assert.True(File.Exists(outputPath), "PDF file was not created");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PdfExport_WithTable_ProducesFile()
    {
        const string adocWithTable = """
            = Table Test

            |===
            | Name | Value

            | A | 1
            | B | 2
            |===
            """;
        var outputPath = Path.Combine(Path.GetTempPath(), $"ascolotl-test-{Guid.NewGuid():N}.pdf");

        try
        {
            var service = new ExportService();
            var result = await service.ExportAsync(adocWithTable, false, null, outputPath, ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

            Assert.True(result.IsSuccess, $"PDF export with table failed: {result.ErrorMessage}");
            Assert.True(File.Exists(outputPath), "PDF file was not created");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PdfExport_BasePathWithHash_ProducesFile()
    {
        // Verifies that # in the basePath path doesn't break parse/include resolution.
        var hashDir = Path.Combine(Path.GetTempPath(), "ascolotl-test#hash");
        Directory.CreateDirectory(hashDir);
        var outputPath = Path.Combine(Path.GetTempPath(), $"ascolotl-hash-test-{Guid.NewGuid():N}.pdf");

        try
        {
            var service = new ExportService();
            var result = await service.ExportAsync(SimpleAdoc, false, hashDir, outputPath, ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);

            Assert.True(result.IsSuccess, $"PDF export with # in basePath failed: {result.ErrorMessage}");
            Assert.True(File.Exists(outputPath), "PDF file was not created");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            try { Directory.Delete(hashDir, true); } catch { }
        }
    }

}
