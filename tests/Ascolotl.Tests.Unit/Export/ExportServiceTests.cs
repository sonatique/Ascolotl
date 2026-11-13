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
            var result = await new ExportService().ExportAsync(Sample, null, path, format, ExportTheme.Asciidoctor, null, null, CancellationToken.None);
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
