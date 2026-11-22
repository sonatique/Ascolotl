using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IRenderService
{
    Task<RenderResult> RenderAsync(string source, string? basePath, bool markdown, bool standalone, CancellationToken cancellationToken,
        string? projectFolder = null);
}
