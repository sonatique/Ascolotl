using AdocNet;
using AdocNet.Converters.Html;
using AdocNet.Parser;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.Resources;

namespace Ascolotl.Export;

/// <summary>
/// Renders a project folder to a static HTML site via AdocNet: one page per
/// document (folder structure mirrored), inter-document links rewritten to
/// .html, the project's other files (images…) copied alongside, and a
/// generated index unless the project ships its own index.adoc.
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
                return ExportResult.Failure(Strings.WebsiteNoDocuments);

            // a.adoc and a.asciidoc would both become a.html: refuse rather than
            // let one page silently replace the other.
            var clash = relativeDocPaths
                .GroupBy(WebsiteBuilder.ToHtmlPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (clash is not null)
                return ExportResult.Failure(
                    string.Format(Strings.WebsiteNameClash, string.Join(Strings.ListAnd, clash), clash.Key));

            // Copied first so a stale guide.html in the project never replaces the rendered page.
            await Task.Run(() => CopyProjectFiles(projectRoot, outputFolder, cancellationToken), cancellationToken);

            var pages = new List<SitePage>();

            foreach (var rel in relativeDocPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = Path.Combine(projectRoot, rel);
                var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);

                // Same parse settings as the preview and the other exports:
                // includes resolve against the document's folder and may leave it, within the project.
                var parsed = DocumentParser.Parse(source, MarkdownFile.Is(rel), Path.GetDirectoryName(sourcePath), projectFolder: projectRoot);
                if (ExportService.FirstNetworkImage(parsed.Document, Path.GetDirectoryName(sourcePath)) is { } image)
                    return ExportResult.Failure(string.Format(Strings.ExportNetworkImage, image));
                await ExportService.DrawDiagramsAsync(parsed.Document, embed: true, cancellationToken);

                var html = new HtmlRenderer().RenderToString(parsed.Document, new HtmlRenderOptions
                {
                    FullDocument = true,
                    Theme = HtmlTheme.Asciidoctor,
                    StemRenderer = Formulas.Renderer,
                });
                html = WebsiteBuilder.RewriteDocLinks(html);

                var relHtml = WebsiteBuilder.ToHtmlPath(rel);
                var outPath = Path.Combine(outputFolder, relHtml.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                await File.WriteAllTextAsync(outPath, html, cancellationToken);

                var title = string.IsNullOrWhiteSpace(parsed.Document.Title)
                    ? Path.GetFileNameWithoutExtension(rel)
                    : parsed.Document.Title;
                pages.Add(new SitePage(relHtml, title));
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
            return ExportResult.Failure(string.Format(Strings.WebsiteExportFailedWithReason, ex.Message));
        }
    }

    /// <summary>
    /// Copies the files pages refer to (images, downloads…): everything in the
    /// project except its documents, dot-folders (.git…) and an output folder inside it.
    /// Nothing when the output folder is the project's: the files are already beside the pages.
    /// </summary>
    private static void CopyProjectFiles(string projectRoot, string outputFolder, CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)) + Path.DirectorySeparatorChar;
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputFolder)) + Path.DirectorySeparatorChar;
        if (output.Equals(root, FilePaths.Comparison)) return;
        var outputInProject = output.StartsWith(root, FilePaths.Comparison);
        // Links are skipped: one in a cloned repo can point at ~/.ssh or anything else.
        var everyRealFile = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*", everyRealFile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(projectRoot, file);
            if (outputInProject && Path.GetFullPath(file).StartsWith(output, FilePaths.Comparison)
                || rel.Split('/', '\\').Any(p => p.StartsWith('.'))
                || Path.GetExtension(rel).ToLowerInvariant() is ".adoc" or ".asciidoc"
                || MarkdownFile.Is(rel))
                continue;

            var target = Path.Combine(outputFolder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
