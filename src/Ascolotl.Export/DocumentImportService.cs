using System.IO.Compression;
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

    /// <summary>The most a Word file may unpack to: a small file can hold gigabytes, and the
    /// import would stall on them.</summary>
    private const long MaxWordUnpackedBytes = 512L * 1024 * 1024;

    private static ImportOutcome ImportWord(string sourcePath, string adocPath, string media)
    {
        if (UnpacksTooLarge(sourcePath))
            return ImportOutcome.Failure(string.Format(Strings.ImportTooLarge,
                ImportFormat.Word.DisplayName(), MaxWordUnpackedBytes / (1024 * 1024)));
        var report = new DocxImporter(new DocxImportOptions { MediaDirectoryName = media })
            .ConvertFile(sourcePath, adocPath).Report;
        return Success(adocPath, report.Fidelity, report.Images, report.Tables,
            report.Issues.Where(i => i.Severity != DocxIssueSeverity.Info));
    }

    /// <summary>Whether a Word file's parts take more than the limit unpacked, as its zip says; false
    /// for a file that is no zip or can't be read, which the import then reports.</summary>
    // ponytail: the sizes the zip declares; one that understates them still reaches the importer.
    private static bool UnpacksTooLarge(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var left = MaxWordUnpackedBytes;
            foreach (var entry in zip.Entries)
                if (entry.Length < 0 || (left -= entry.Length) < 0) return true; // counted down: no overflow
            return false;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
