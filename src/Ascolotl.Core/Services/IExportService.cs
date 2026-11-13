using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IExportService
{
    /// <summary>
    /// Render the given AsciiDoc source to <paramref name="format"/> and write
    /// it to <paramref name="outputPath"/>. <paramref name="basePath"/> is the
    /// directory used for resolving relative <c>include::</c> directives and
    /// image references. <paramref name="theme"/> is the built-in look
    /// (export profiles) for HTML and PDF. <paramref name="stylePath"/> is the
    /// user's asciidoctor-pdf theme (null for none); a document's own
    /// <c>:pdf-theme:</c> attribute overrides it, and either one replaces
    /// <paramref name="theme"/> for PDF and is layered over it for HTML/EPUB.
    /// <paramref name="pdfTitle"/> replaces a PDF's Title metadata only
    /// (null keeps the document title).
    /// </summary>
    Task<ExportResult> ExportAsync(
        string source, string? basePath, string outputPath, ExportFormat format,
        ExportTheme theme, string? stylePath, string? pdfTitle, CancellationToken cancellationToken);
}
