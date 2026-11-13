using AdocNet.Importers.Docx;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Export;

/// <summary>
/// Word (.docx) import backed by AdocNet's pure-managed importer: reads the
/// OOXML package, converts it to the AsciiDoc AST, emits source, and writes
/// referenced images into a media folder beside the output.
/// </summary>
public sealed class DocxImportService : IDocxImportService
{
    public Task<DocxImportOutcome> ImportAsync(
        string docxPath, string adocPath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = new DocxImporter().ConvertFile(docxPath, adocPath);
                var report = result.Report;

                return new DocxImportOutcome
                {
                    IsSuccess = true,
                    OutputPath = adocPath,
                    Fidelity = report.Fidelity,
                    Images = report.Images,
                    Tables = report.Tables,
                    // Approximations and losses only — the informational
                    // entries would drown the ones that need attention.
                    Issues =
                    [
                        .. report.Issues
                            .Where(i => i.Severity != DocxIssueSeverity.Info)
                            .Select(i => i.ToString())
                    ],
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DocxImportException ex)
            {
                return DocxImportOutcome.Failure($"Not a readable Word document: {ex.Message}");
            }
            catch (Exception ex)
            {
                return DocxImportOutcome.Failure($"Word import failed: {ex.Message}");
            }
        }, cancellationToken);
    }
}
