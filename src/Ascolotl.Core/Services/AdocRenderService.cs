using AdocNet.Ast;
using AdocNet.Parser;
using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

/// <summary>
/// In-process AsciiDoc parse for the live preview, backed by AdocNet. The
/// preview draws the parsed document itself, so no HTML is produced here.
/// </summary>
public sealed class AdocRenderService : IRenderService
{
    public Task<RenderResult> RenderAsync(string source, string? basePath, bool standalone, CancellationToken cancellationToken)
    {
        // Render is CPU-bound, fully synchronous in AdocNet, and typically
        // completes in 1–70ms on real-world docs. Offload to a worker so the
        // UI thread doesn't block on the parse for very large documents.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // The "Show Title & TOC" toggle (passed as `standalone`) drives
                // the auto-generated table of contents: only request it when the
                // toggle is on. (When a document sets `:toc:` in its own header
                // this default is overridden — see the TocNode strip below.)
                var attributes = new Dictionary<string, string>();
                if (standalone)
                    attributes["toc"] = "auto";

                var parsed = AdocParser.Parse(source, LocalIncludeReader.ParseOptions(basePath, attributes));

                // When the toggle is off, drop the table of contents even if the
                // document's own header set `:toc:` — AdocNet honours the
                // document attribute over the API one, so the only reliable way
                // to suppress it is to remove the TocNode from the AST. The
                // preview, HTML and PDF all build from this AST, so this keeps
                // them consistent. (The document title is handled separately by
                // each renderer, since DocumentLayout.Title is always populated.)
                if (!standalone)
                {
                    for (var i = parsed.Document.Children.Count - 1; i >= 0; i--)
                    {
                        if (parsed.Document.Children[i] is TocNode)
                            parsed.Document.RemoveChildAt(i);
                    }
                }

                return RenderResult.Success(parsed.Document, parsed.LineOrigins, parsed.Diagnostics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return RenderResult.Failure(ex.Message);
            }
        }, cancellationToken);
    }
}
