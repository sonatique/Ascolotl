using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IDocumentImportService
{
    /// <summary>
    /// Converts <paramref name="sourcePath"/>, read as <paramref name="format"/>,
    /// to AsciiDoc at <paramref name="adocPath"/>, extracting images into a media
    /// folder beside it. Never throws for a bad or unreadable file — failures
    /// come back as a failed outcome.
    /// </summary>
    Task<ImportOutcome> ImportAsync(ImportFormat format, string sourcePath, string adocPath, CancellationToken cancellationToken);
}
