namespace Ascolotl.Tests.Unit.Models;

using Ascolotl.Core.Models;

/// <summary>
/// Tests for <see cref="ExportProfile"/> — output-path resolution for
/// one-click profile exports.
/// </summary>
public class ExportProfileTests
{
    [Fact]
    public void Resolve_ProfileFolder_UsesDocumentBaseName()
    {
        var p = new ExportProfile { Format = ExportFormat.Pdf, OutputFolder = @"C:\out" };
        var path = p.ResolveOutputPath(@"C:\docs\guide.adoc", "guide.adoc");
        Assert.Equal(Path.Combine(@"C:\out", "guide.pdf"), path);
    }

    [Fact]
    public void Resolve_NoProfileFolder_UsesDocumentFolder()
    {
        var p = new ExportProfile { Format = ExportFormat.Html };
        var path = p.ResolveOutputPath(@"C:\docs\guide.adoc", "guide.adoc");
        Assert.Equal(Path.Combine(@"C:\docs", "guide.html"), path);
    }

    [Fact]
    public void Resolve_UnsavedDocument_WithProfileFolder_UsesDisplayName()
    {
        var p = new ExportProfile { Format = ExportFormat.Epub, OutputFolder = @"C:\out" };
        var path = p.ResolveOutputPath(null, "Untitled-1");
        Assert.Equal(Path.Combine(@"C:\out", "Untitled-1.epub"), path);
    }

    [Fact]
    public void Resolve_UnsavedDocument_NoFolder_ReturnsNull()
    {
        var p = new ExportProfile();
        Assert.Null(p.ResolveOutputPath(null, "Untitled-1"));
    }

    [Fact]
    public void Resolve_FormatDrivesExtension()
    {
        var p = new ExportProfile { Format = ExportFormat.DocBook, OutputFolder = @"C:\out" };
        Assert.EndsWith(".xml", p.ResolveOutputPath(@"C:\d\a.adoc", "a.adoc")!);
    }

    [Fact]
    public void Resolve_BlankBaseName_FallsBack()
    {
        var p = new ExportProfile { Format = ExportFormat.Pdf, OutputFolder = @"C:\out" };
        var path = p.ResolveOutputPath(null, "");
        Assert.Equal(Path.Combine(@"C:\out", "document.pdf"), path);
    }
}
