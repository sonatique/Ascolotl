namespace Ascolotl.Tests.Unit.Export;

using System.IO.Compression;
using Ascolotl.Core.Models;
using Ascolotl.Export;

/// <summary>
/// Tests for <see cref="DocumentImportService"/> against real files: a minimal
/// hand-built .docx package converts, and malformed input fails gracefully
/// rather than throwing.
/// </summary>
public class DocumentImportServiceTests : IDisposable
{
    private readonly string _dir;

    public DocumentImportServiceTests()
        => _dir = Directory.CreateTempSubdirectory("ascolotl-docx-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Builds the smallest OOXML package that is a valid Word document.</summary>
    private string WriteMinimalDocx(string bodyXml)
    {
        var path = Path.Combine(_dir, "sample.docx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        Write(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);

        Write(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        Write(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>{bodyXml}</w:body>
            </w:document>
            """);

        return path;

        static void Write(ZipArchive zip, string name, string content)
        {
            using var stream = zip.CreateEntry(name).Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
    }

    private static string Paragraph(string text, string? style = null)
    {
        var props = style is null ? "" : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>";
        return $"<w:p>{props}<w:r><w:t>{text}</w:t></w:r></w:p>";
    }

    [Fact]
    public async Task Imports_a_word_document_to_asciidoc()
    {
        var docx = WriteMinimalDocx(Paragraph("The Guide", "Heading1") + Paragraph("Body text here."));
        var adoc = Path.Combine(_dir, "out", "guide.adoc");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Word, docx, adoc, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.True(File.Exists(adoc));

        var text = File.ReadAllText(adoc);
        Assert.Contains("The Guide", text);
        Assert.Contains("Body text here.", text);
        Assert.Equal(adoc, outcome.OutputPath);
        Assert.InRange(outcome.Fidelity, 0, 1);
    }

    /// <summary>A .docx whose only content is one picture, stored as word/media/image1.png.</summary>
    private string WriteDocxWithImage(string name, byte[] png)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="png" ContentType="image/png"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        Write(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        Write(zip, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdImg" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
            </Relationships>
            """);
        Write(zip, "word/document.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
                xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
              <w:body><w:p><w:r><w:drawing><wp:inline>
                <wp:extent cx="952500" cy="952500"/><wp:docPr id="1" name="Picture 1"/>
                <a:graphic><a:graphicData><pic:pic><pic:blipFill><a:blip r:embed="rIdImg"/></pic:blipFill></pic:pic></a:graphicData></a:graphic>
              </wp:inline></w:drawing></w:r></w:p></w:body>
            </w:document>
            """);
        using (var stream = zip.CreateEntry("word/media/image1.png").Open())
            stream.Write(png);
        return path;

        static void Write(ZipArchive zip, string name, string content)
        {
            using var stream = zip.CreateEntry(name).Open();
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }
    }

    [Fact]
    public async Task Two_imports_into_one_folder_keep_their_own_images()
    {
        byte[] first = [.. Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")];
        byte[] second = [.. first, 0]; // trailing byte: same picture, different file
        var outDir = Path.Combine(_dir, "out");
        var a = Path.Combine(outDir, "a.adoc");
        var b = Path.Combine(outDir, "b.adoc");

        var service = new DocumentImportService();
        Assert.True((await service.ImportAsync(ImportFormat.Word, WriteDocxWithImage("a.docx", first), a, CancellationToken.None)).IsSuccess);
        Assert.True((await service.ImportAsync(ImportFormat.Word, WriteDocxWithImage("b.docx", second), b, CancellationToken.None)).IsSuccess);

        var target = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(a), @"image::?([^\[]+)\[").Groups[1].Value;
        Assert.NotEmpty(target);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(outDir, target)));
    }

    [Fact]
    public async Task Malformed_file_fails_without_throwing()
    {
        var fake = Path.Combine(_dir, "not-really.docx");
        File.WriteAllText(fake, "this is plain text, not a zip package");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Word,
            fake, Path.Combine(_dir, "out.adoc"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public async Task Missing_file_fails_without_throwing()
    {
        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Word,
            Path.Combine(_dir, "nope.docx"), Path.Combine(_dir, "out.adoc"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
    }
    [Fact]
    public async Task Markdown_converts_to_AsciiDoc()
    {
        var md = Path.Combine(_dir, "notes.md");
        File.WriteAllText(md, "# Notes\n\nSome **bold** text.\n\n- one\n- two\n");
        var adoc = Path.Combine(_dir, "notes.adoc");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Markdown, md, adoc, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        var text = File.ReadAllText(adoc);
        Assert.StartsWith("= Notes", text);
        Assert.Contains("*bold*", text);
        Assert.Contains("* two", text);
    }

    [Fact]
    public async Task Html_embedded_image_lands_in_the_documents_media_folder()
    {
        var html = Path.Combine(_dir, "page.html");
        // A 1x1 PNG as a data URI: the importer must write it out, not inline it.
        File.WriteAllText(html, "<html><body><h1>Page</h1><p>Hi</p><img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\"></body></html>");
        var adoc = Path.Combine(_dir, "page.adoc");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Html, html, adoc, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Equal(1, outcome.Images);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "page-media"), "*", SearchOption.AllDirectories));
        Assert.Contains("image::page-media/", File.ReadAllText(adoc));
    }

    [Fact]
    public async Task Html_remote_image_stays_a_link_and_is_not_fetched()
    {
        var html = Path.Combine(_dir, "remote.html");
        File.WriteAllText(html, "<html><body><p>Hi</p><img src=\"http://127.0.0.1:9/x.png\"></body></html>");
        var adoc = Path.Combine(_dir, "remote.adoc");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Html, html, adoc, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(_dir, "remote-media")));
        Assert.Contains("http://127.0.0.1:9/x.png", File.ReadAllText(adoc));
    }

    [Fact]
    public async Task Pdf_exported_by_Ascolotl_imports_back()
    {
        var pdf = Path.Combine(_dir, "round.pdf");
        var export = await new ExportService().ExportAsync("= Round Trip\n\nA paragraph that survives.\n", null, pdf,
            ExportFormat.Pdf, ExportTheme.Asciidoctor, null, null, CancellationToken.None);
        Assert.True(export.IsSuccess, export.ErrorMessage);
        var adoc = Path.Combine(_dir, "round.adoc");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Pdf, pdf, adoc, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.ErrorMessage);
        Assert.Contains("A paragraph that survives.", File.ReadAllText(adoc));
    }

    [Fact]
    public async Task Malformed_pdf_fails_without_throwing()
    {
        var fake = Path.Combine(_dir, "broken.pdf");
        File.WriteAllText(fake, "not a pdf");

        var outcome = await new DocumentImportService().ImportAsync(ImportFormat.Pdf, fake, Path.Combine(_dir, "out.adoc"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.StartsWith("Not a readable PDF file", outcome.ErrorMessage);
    }
}
