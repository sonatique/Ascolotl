using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IDocxImportService
{
    /// <summary>
    /// Converts a Word document to AsciiDoc at <paramref name="adocPath"/>,
    /// extracting images into a media folder beside it. Never throws for a
    /// bad or unreadable file — failures come back as a failed outcome.
    /// </summary>
    Task<DocxImportOutcome> ImportAsync(string docxPath, string adocPath, CancellationToken cancellationToken);
}
