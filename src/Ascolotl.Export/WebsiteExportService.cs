using AdocNet;
using AdocNet.Converters.Html;
using AdocNet.Parser;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Export;

/// <summary>
/// Renders a project folder to a static HTML site via AdocNet: one page per
/// document (folder structure mirrored), inter-document links rewritten to
/// .html, and a generated index unless the project ships its own index.adoc.
/// </summary>
public sealed class WebsiteExportService : IWebsiteExportService
{
    public async Task<ExportResult> ExportAsync(
        string projectRoot,
        IReadOnlyList<string> relativeDocPaths,
        string outputFolder,
        CancellationToken cancellationToken)
    {
        try
        {
            if (relativeDocPaths.Count == 0)
                return ExportResult.Failure("The project has no documents to export.");

            var pages = new List<SitePage>();

            foreach (var rel in relativeDocPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = Path.Combine(projectRoot, rel);
                var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);

                // Includes/images resolve relative to the document's own folder.
                var parseOptions = new ParseOptions
                {
                    BaseDirectory = Path.GetDirectoryName(sourcePath),
                };
                var parsed = AdocParser.Parse(source, parseOptions);

                var html = new HtmlRenderer().RenderToString(parsed.Document, new HtmlRenderOptions
                {
                    FullDocument = true,
                    Theme = HtmlTheme.Asciidoctor,
                });
                html = WebsiteBuilder.RewriteDocLinks(html);

                var relHtml = WebsiteBuilder.ToHtmlPath(rel);
                var outPath = Path.Combine(outputFolder, relHtml.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                await File.WriteAllTextAsync(outPath, html, cancellationToken);

                pages.Add(new SitePage(relHtml, WebsiteBuilder.ExtractTitle(source, rel)));
            }

            if (!WebsiteBuilder.HasOwnIndex(relativeDocPaths))
            {
                var siteTitle = Path.GetFileName(projectRoot.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(siteTitle)) siteTitle = "Documentation";

                var index = WebsiteBuilder.BuildIndexHtml(siteTitle, pages);
                await File.WriteAllTextAsync(
                    Path.Combine(outputFolder, "index.html"), index, cancellationToken);
            }

            return ExportResult.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ExportResult.Failure($"Website export failed: {ex.Message}");
        }
    }
}
