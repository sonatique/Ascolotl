using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IExportService
{
    /// <summary>
    /// Render the given AsciiDoc (or, when <paramref name="markdown"/>, Markdown)
    /// source to <paramref name="format"/> and write
    /// it to <paramref name="outputPath"/>. <paramref name="basePath"/> is the
    /// directory used for resolving relative <c>include::</c> directives and
    /// image references. <paramref name="theme"/> is the built-in look
    /// (export profiles) for HTML and PDF. <paramref name="stylePath"/> is the
    /// user's asciidoctor-pdf theme (null for none); a document's own
    /// <c>:pdf-theme:</c> attribute overrides it, and either one replaces
    /// <paramref name="theme"/> for PDF and is layered over it for HTML/EPUB.
    /// <paramref name="pdf"/> holds PDF-only settings (title override,
    /// accessibility); null means the defaults. <paramref name="projectFolder"/> is
    /// the folder open in the app, which includes may reach across.
    /// </summary>
    Task<ExportResult> ExportAsync(
        string source, bool markdown, string? basePath, string outputPath, ExportFormat format,
        ExportTheme theme, string? stylePath, PdfExportOptions? pdf, CancellationToken cancellationToken,
        string? projectFolder = null);
}
