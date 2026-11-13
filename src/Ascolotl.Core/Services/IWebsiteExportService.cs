using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IWebsiteExportService
{
    /// <summary>
    /// Export the given project documents (paths relative to
    /// <paramref name="projectRoot"/>) as a static HTML site under
    /// <paramref name="outputFolder"/>, mirroring the folder structure,
    /// rewriting inter-document links, and generating an index page unless the
    /// project has its own index.adoc.
    /// </summary>
    Task<ExportResult> ExportAsync(
        string projectRoot,
        IReadOnlyList<string> relativeDocPaths,
        string outputFolder,
        CancellationToken cancellationToken);
}
