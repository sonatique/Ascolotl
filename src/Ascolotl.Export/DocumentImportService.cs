using AdocNet.Importers;
using AdocNet.Importers.Docx;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.Resources;

namespace Ascolotl.Export;

/// <summary>
/// Word, Markdown, HTML and PDF import backed by AdocNet's managed importers:
/// converts the file to AsciiDoc, writes it, and writes referenced images into
/// a media folder beside the output.
/// </summary>
public sealed class DocumentImportService : IDocumentImportService
{
    public Task<ImportOutcome> ImportAsync(
        ImportFormat format, string sourcePath, string adocPath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Media folder per document, so a second import into the same folder
            // does not overwrite the first one's image1.png.
            var media = Path.GetFileNameWithoutExtension(adocPath) + "-media";
            try
            {
                return format == ImportFormat.Word
                    ? ImportWord(sourcePath, adocPath, media)
                    : Import(format, sourcePath, adocPath, media);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is DocxImportException or ImportException)
            {
                return ImportOutcome.Failure(string.Format(Strings.ImportNotReadable, format.DisplayName(), ex.Message), ex);
            }
            catch (Exception ex)
            {
                return ImportOutcome.Failure(string.Format(Strings.ImportFailedWithReason, format.DisplayName(), ex.Message), ex);
            }
        }, cancellationToken);
    }

    private static ImportOutcome ImportWord(string sourcePath, string adocPath, string media)
    {
        var report = new DocxImporter(new DocxImportOptions { MediaDirectoryName = media })
            .ConvertFile(sourcePath, adocPath).Report;
        return Success(adocPath, report.Fidelity, report.Images, report.Tables,
            report.Issues.Where(i => i.Severity != DocxIssueSeverity.Info));
    }

    private static ImportOutcome Import(ImportFormat format, string sourcePath, string adocPath, string media)
    {
        var options = new ImportOptions { MediaDirectoryName = media };
        DocumentImporter importer = format switch
        {
            ImportFormat.Markdown => new MarkdownImporter(options),
            ImportFormat.Html => new HtmlImporter(options),
            ImportFormat.Pdf => new PdfImporter(options),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a document import format"),
        };
        var report = importer.ConvertFile(sourcePath, adocPath).Report;
        return Success(adocPath, report.Fidelity, report.Images, report.Tables,
            report.Issues.Where(i => i.Severity != ImportIssueSeverity.Info));
    }

    /// <param name="issues">Approximations and losses only: informational entries
    /// would drown the ones that need attention.</param>
    private static ImportOutcome Success(string adocPath, double fidelity, int images, int tables, IEnumerable<object> issues) => new()
    {
        IsSuccess = true,
        OutputPath = adocPath,
        Fidelity = fidelity,
        Images = images,
        Tables = tables,
        Issues = [.. issues.Select(i => i.ToString()!)],
    };
}
