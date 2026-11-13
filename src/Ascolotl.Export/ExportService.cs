using AdocNet;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Export;

/// <summary>
/// In-process document export backed by AdocNet's pure-managed converters
/// (PDF, HTML, DocBook, EPUB). No Chromium, no headless browser, no external
/// process. Parses the AsciiDoc source once and renders straight to the target
/// format's bytes on a worker thread.
/// </summary>
public sealed class ExportService : IExportService
{
    public Task<ExportResult> ExportAsync(
        string source, string? basePath, string outputPath, ExportFormat format,
        ExportTheme theme, string? stylePath, string? pdfTitle, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parseOptions = new ParseOptions
                {
                    BaseDirectory = basePath,
                    IncludeMaxDepth = 10,
                    AllowUriRead = false,
                    // Asciidoctor's CLI default: includes may leave the document's
                    // folder (e.g. a shared ../../copyright.adoc).
                    SafeMode = SafeMode.Unsafe,
                };

                var parsed = AdocParser.Parse(source, parseOptions);

                // The style (the document's :pdf-theme:, else the user's choice)
                // drives PDF natively and HTML/EPUB through generated CSS.
                var docDir = basePath ?? Directory.GetCurrentDirectory();
                var attributes = parsed.Document.Attributes;
                var themePath = format == ExportFormat.DocBook
                    ? null
                    : PdfThemeResolver.ResolveThemePath(attributes, docDir, stylePath);
                if (themePath is not null && !File.Exists(themePath))
                    return ExportResult.Failure($"Style not found: {themePath}");
                var styleCss = themePath is not null && format is ExportFormat.Html or ExportFormat.Epub
                    ? StyleCss.From(PdfThemeResolver.Load(themePath, attributes, docDir))
                    : null;

                using var ms = new MemoryStream();
                switch (format)
                {
                    case ExportFormat.Pdf:
                    case ExportFormat.PdfLandscape:
                    {
                        // The style (asciidoctor-pdf theme) replaces the built-in look.
                        var page = themePath is not null
                            ? PdfThemeResolver.Load(themePath, attributes, docDir)
                            : ExportThemes.Pdf(theme);
                        // Landscape (~40% more width, for wide tables) swaps the
                        // page sides; the copy leaves the loaded options untouched.
                        var landscape = format == ExportFormat.PdfLandscape && page.PageWidth < page.PageHeight;
                        var options = new PdfRenderOptions(page)
                        {
                            PageWidth = landscape ? page.PageHeight : page.PageWidth,
                            PageHeight = landscape ? page.PageWidth : page.PageHeight,
                            MetadataTitle = pdfTitle,
                        };
                        new PdfRenderer().Render(parsed.Document, ms, options);
                        break;
                    }
                    case ExportFormat.Html:
                        new HtmlRenderer().Render(parsed.Document, ms, new HtmlRenderOptions
                        {
                            FullDocument = true,
                            Theme = ExportThemes.Html(theme),
                            BaseDirectory = basePath,
                            CustomCss = styleCss,
                        });
                        break;
                    case ExportFormat.DocBook:
                        new DocBookRenderer().Render(parsed.Document, ms, RenderOptions.Default);
                        break;
                    case ExportFormat.Epub:
                        new EpubRenderer().Render(parsed.Document, ms, new EpubRenderOptions { CustomCss = styleCss });
                        break;
                    default:
                        return ExportResult.Failure($"Unsupported export format: {format}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, ms.ToArray());

                return File.Exists(outputPath)
                    ? ExportResult.Success()
                    : ExportResult.Failure($"{format.DisplayName()} file was not created.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex) when (IsInUse(ex))
            {
                return ExportResult.Failure(
                    $"{Path.GetFileName(outputPath)} is open in another program, so it can't be replaced. " +
                    "Close it there, then export again.");
            }
            catch (Exception ex)
            {
                return ExportResult.Failure($"{format.DisplayName()} export failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>Windows sharing or lock violation: another program holds the file open.</summary>
    private static bool IsInUse(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;
}
