namespace Ascolotl.Tests.Unit.Export;

using System.IO.Compression;
using Ascolotl.Export;

/// <summary>
/// Tests for <see cref="DocxImportService"/> against real files: a minimal
/// hand-built .docx package converts, and malformed input fails gracefully
/// rather than throwing.
/// </summary>
public class DocxImportServiceTests : IDisposable
{
    private readonly string _dir;

    public DocxImportServiceTests()
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

        var outcome = await new DocxImportService().ImportAsync(docx, adoc, CancellationToken.None);

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

        var service = new DocxImportService();
        Assert.True((await service.ImportAsync(WriteDocxWithImage("a.docx", first), a, CancellationToken.None)).IsSuccess);
        Assert.True((await service.ImportAsync(WriteDocxWithImage("b.docx", second), b, CancellationToken.None)).IsSuccess);

        var target = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(a), @"image::?([^\[]+)\[").Groups[1].Value;
        Assert.NotEmpty(target);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(outDir, target)));
    }

    [Fact]
    public async Task Malformed_file_fails_without_throwing()
    {
        var fake = Path.Combine(_dir, "not-really.docx");
        File.WriteAllText(fake, "this is plain text, not a zip package");

        var outcome = await new DocxImportService().ImportAsync(
            fake, Path.Combine(_dir, "out.adoc"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public async Task Missing_file_fails_without_throwing()
    {
        var outcome = await new DocxImportService().ImportAsync(
            Path.Combine(_dir, "nope.docx"), Path.Combine(_dir, "out.adoc"), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
    }
}
