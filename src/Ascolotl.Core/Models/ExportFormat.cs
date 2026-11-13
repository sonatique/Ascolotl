namespace Ascolotl.Core.Models;

/// <summary>A document format AdocNet can export to.</summary>
public enum ExportFormat
{
    Pdf,

    /// <summary>PDF on a landscape (rotated) A4 page — handy for wide tables
    /// and wide content generally.</summary>
    PdfLandscape,

    Html,
    DocBook,
    Epub,
}

public static class ExportFormatExtensions
{
    /// <summary>File extension (no dot) for the format.</summary>
    public static string Extension(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "pdf",
        ExportFormat.PdfLandscape => "pdf",
        ExportFormat.Html => "html",
        ExportFormat.DocBook => "xml",
        ExportFormat.Epub => "epub",
        _ => "txt",
    };

    /// <summary>Save-dialog filter label for the format.</summary>
    public static string FilterName(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "PDF Document",
        ExportFormat.PdfLandscape => "PDF Document",
        ExportFormat.Html => "HTML Document",
        ExportFormat.DocBook => "DocBook XML",
        ExportFormat.Epub => "EPUB Book",
        _ => "File",
    };

    /// <summary>Human-facing name used in status messages.</summary>
    public static string DisplayName(this ExportFormat format) => format switch
    {
        ExportFormat.Pdf => "PDF",
        ExportFormat.PdfLandscape => "PDF (Landscape)",
        ExportFormat.Html => "HTML",
        ExportFormat.DocBook => "DocBook",
        ExportFormat.Epub => "EPUB",
        _ => format.ToString(),
    };
}
