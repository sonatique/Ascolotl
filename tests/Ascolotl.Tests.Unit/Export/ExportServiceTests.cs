namespace Ascolotl.Tests.Unit.Export;

using Ascolotl.Core.Models;
using Ascolotl.Export;

/// <summary>
/// Exercises <see cref="ExportService"/> end-to-end for every supported format,
/// rendering a real document and verifying the output file's basic signature.
/// </summary>
public class ExportServiceTests
{
    private const string Sample = """
        = Export Test

        Intro paragraph with *bold* and _italic_.

        == Section

        |===
        | Name | Value
        | A | 1
        |===
        """;

    private static async Task<(ExportResult result, byte[] bytes, string path)> ExportAsync(ExportFormat format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ascolotl-export-{Guid.NewGuid():N}.{format.Extension()}");
        try
        {
            var result = await new ExportService().ExportAsync(Sample, false, null, path, format, ExportTheme.Asciidoctor, null, null, CancellationToken.None);
            var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path) : [];
            return (result, bytes, path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(ExportFormat.Pdf)]
    [InlineData(ExportFormat.PdfLandscape)]
    [InlineData(ExportFormat.Html)]
    [InlineData(ExportFormat.DocBook)]
    [InlineData(ExportFormat.Epub)]
    public async Task Export_ProducesNonEmptyFile(ExportFormat format)
    {
        var (result, bytes, _) = await ExportAsync(format);

        Assert.True(result.IsSuccess, $"{format} export failed: {result.ErrorMessage}");
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Export_PdfLandscape_HasPdfSignatureAndPdfExtension()
    {
        Assert.Equal("pdf", ExportFormat.PdfLandscape.Extension());
        var (_, bytes, _) = await ExportAsync(ExportFormat.PdfLandscape);
        Assert.True(bytes.Length > 4 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P'
            && bytes[2] == (byte)'D' && bytes[3] == (byte)'F', "missing %PDF header");
    }

    /// <summary>Exports <paramref name="source"/> to PDF in a temp folder holding a small PNG.</summary>
    private static async Task<(ExportResult Result, string Text)> ExportPdfAsync(string source, PdfExportOptions? pdf, ExportFormat format = ExportFormat.Pdf)
    {
        var dir = Directory.CreateTempSubdirectory("ascolotl-pdfua-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "pic.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
            var path = Path.Combine(dir, "out.pdf");
            var result = await new ExportService().ExportAsync(source, false, dir, path, format, ExportTheme.Asciidoctor, null, pdf, CancellationToken.None);
            return (result, File.Exists(path) ? System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path)) : "");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(ExportFormat.Pdf)]
    [InlineData(ExportFormat.PdfLandscape)]
    public async Task Export_AccessiblePdf_IsTaggedPdfUa_PlainPdfIsNot(ExportFormat format)
    {
        var (accessible, tagged) = await ExportPdfAsync(Sample, new PdfExportOptions(Accessible: true), format);
        var (plain, untagged) = await ExportPdfAsync(Sample, null, format);

        Assert.True(accessible.IsSuccess, accessible.ErrorMessage);
        Assert.Contains("pdfuaid:part", tagged);
        Assert.Contains("/MarkInfo", tagged);
        Assert.True(plain.IsSuccess, plain.ErrorMessage);
        Assert.DoesNotContain("pdfuaid:part", untagged);
    }

    [Fact]
    public async Task Export_AccessiblePdf_ReportsImagesWithoutAltText()
    {
        const string source = "= Pictures\n\nimage::pic.png[]\n\nimage::pic.png[A described picture]\n";

        var (accessible, _) = await ExportPdfAsync(source, new PdfExportOptions(Accessible: true));
        var (plain, _) = await ExportPdfAsync(source, null);

        Assert.True(accessible.IsSuccess, accessible.ErrorMessage);
        Assert.Single(accessible.Warnings);
        Assert.Empty(plain.Warnings);
    }

    [Fact]
    public async Task Export_Pdf_HasPdfSignature()
    {
        var (_, bytes, _) = await ExportAsync(ExportFormat.Pdf);
        Assert.True(bytes.Length > 4 && bytes[0] == (byte)'%' && bytes[1] == (byte)'P'
            && bytes[2] == (byte)'D' && bytes[3] == (byte)'F', "missing %PDF header");
    }

    [Fact]
    public async Task Export_Epub_IsZipArchive()
    {
        var (_, bytes, _) = await ExportAsync(ExportFormat.Epub);
        Assert.True(bytes.Length > 2 && bytes[0] == 0x50 && bytes[1] == 0x4B, "missing PK (zip) header");
    }

    [Fact]
    public async Task Export_Html_IsStandaloneDocument()
    {
        var (_, bytes, _) = await ExportAsync(ExportFormat.Html);
        var html = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Section", html);
    }

    [Fact]
    public async Task Export_DocBook_IsXml()
    {
        var (_, bytes, _) = await ExportAsync(ExportFormat.DocBook);
        var xml = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("<?xml", xml.TrimStart());
    }
}
