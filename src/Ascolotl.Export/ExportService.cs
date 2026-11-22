using AdocNet;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Ast;
using AdocNet.Layout;
using AdocNet.Layout.Builders;
using AdocNet.Parser;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.Resources;

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
        string source, bool markdown, string? basePath, string outputPath, ExportFormat format,
        ExportTheme theme, string? stylePath, PdfExportOptions? pdf, CancellationToken cancellationToken,
        string? projectFolder = null)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Includes may leave the document's folder (e.g. a shared
                // ../../copyright.adoc), within its project or repository.
                var parsed = DocumentParser.Parse(source, markdown, basePath, projectFolder: projectFolder);
                if (FirstNetworkImage(parsed.Document, basePath) is { } image)
                    return ExportResult.Failure(string.Format(Strings.ExportNetworkImage, image));

                // The style (the document's :pdf-theme:, else the user's choice)
                // drives PDF natively and HTML/EPUB through generated CSS.
                var docDir = basePath ?? Directory.GetCurrentDirectory();
                var attributes = parsed.Document.Attributes;
                var themePath = format == ExportFormat.DocBook
                    ? null
                    : PdfThemeResolver.ResolveThemePath(attributes, docDir, stylePath);
                if (themePath is not null && !File.Exists(themePath))
                    return ExportResult.Failure(string.Format(Strings.ExportStyleNotFound, themePath));
                var styleCss = themePath is not null && format is ExportFormat.Html or ExportFormat.Epub
                    ? StyleCss.From(PdfThemeResolver.Load(themePath, attributes, docDir))
                    : null;

                using var ms = new MemoryStream();
                List<string> warnings = [];
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
                            MetadataTitle = pdf?.Title,
                            Accessibility = pdf?.Accessible == true,
                            // AdocNet's PDF renderer loads images from here and
                            // does not apply :imagesdir: itself.
                            BaseDirectory = ImageBase(basePath, attributes.GetValueOrDefault("imagesdir", "")),
                        };
                        var renderer = new PdfRenderer();
                        renderer.Render(parsed.Document, ms, options);
                        warnings = [.. renderer.Warnings];
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
                        return ExportResult.Failure(string.Format(Strings.ExportUnsupportedFormat, format));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, ms.ToArray());

                return File.Exists(outputPath)
                    ? ExportResult.Success(warnings)
                    : ExportResult.Failure(string.Format(Strings.ExportFileNotCreated, format.DisplayName()));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex) when (IsInUse(ex))
            {
                return ExportResult.Failure(
                    string.Format(Strings.ExportFileInUse, Path.GetFileName(outputPath)));
            }
            catch (Exception ex)
            {
                return ExportResult.Failure(string.Format(Strings.ExportFailedWithReason, format.DisplayName(), ex.Message));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// The first image the document shows from a network or device path, or null.
    /// The converters open image paths as given, and on Windows opening \\server
    /// sends the user's sign-in there; an exported page would do the same when viewed.
    /// </summary>
    public static string? FirstNetworkImage(DocumentNode document, string? basePath)
    {
        var imagesDir = document.Attributes.GetValueOrDefault("imagesdir", "");
        return ImageTargets(new LayoutBuilder().Build(document).Children).FirstOrDefault(target =>
        {
            var path = target;
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) return false; // a web or data: address is not opened as a path
                path = uri.LocalPath; // file://server/share is \\server\share
            }
            return LocalIncludeReader.IsBlocked(Path.Combine(basePath ?? "", imagesDir, path), basePath);
        });
    }

    // Every layout kind AdocNet 1.0.43 has (see LayoutEquality); the rest hold no images.
    private static IEnumerable<string> ImageTargets(IEnumerable<BlockLayout> blocks) => blocks.SelectMany(b => b switch
    {
        ImageLayout i => [i.Target],
        ParagraphLayout p => ImageTargets(p.Inlines),
        HeadingLayout h => ImageTargets(h.Inlines),
        AdmonitionLayout a => ImageTargets(a.Blocks),
        QuoteLayout q => ImageTargets(q.Blocks),
        ListLayout l => ImageTargets(l.Items),
        ListItemLayout li => ImageTargets(li.Inlines).Concat(ImageTargets(li.Blocks)),
        DescriptionListLayout d => d.Items.SelectMany(i => ImageTargets(i.Term).Concat(ImageTargets(i.Description))),
        TableLayout t => t.Rows.SelectMany(r => r.Cells).SelectMany(c => ImageTargets(c.Inlines)),
        _ => [],
    });

    private static IEnumerable<string> ImageTargets(IEnumerable<InlineLayout> inlines) => inlines.SelectMany(i => i switch
    {
        ImageRun r => [r.Target],
        LinkRun x => ImageTargets(x.Children),
        BoldRun x => ImageTargets(x.Children),
        ItalicRun x => ImageTargets(x.Children),
        MonoRun x => ImageTargets(x.Children),
        SubscriptRun x => ImageTargets(x.Children),
        SuperscriptRun x => ImageTargets(x.Children),
        StrikethroughRun x => ImageTargets(x.Children),
        HighlightRun x => ImageTargets(x.Children),
        _ => [],
    });

    /// <summary>Windows sharing or lock violation: another program holds the file open.</summary>
    private static bool IsInUse(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    /// <summary>Folder images load from; never a network or device path (:imagesdir: \server would leak credentials).</summary>
    private static string? ImageBase(string? basePath, string imagesDir)
    {
        if (basePath is null) return null;
        var dir = Path.Combine(basePath, imagesDir);
        return LocalIncludeReader.IsBlocked(dir, basePath) ? null : dir;
    }
}
