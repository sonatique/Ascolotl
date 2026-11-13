using System.Text;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;
using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

/// <summary>
/// In-process AsciiDoc renderer backed by AdocNet. No JavaScript engine,
/// no WebView, no external binaries — runs anywhere managed code runs.
/// Source-line markers are derived from the AST instead of regex-scanning
/// the source text.
/// </summary>
public sealed class AdocRenderService : IRenderService
{
    private readonly HtmlRenderer _renderer = new();

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
                var attributes = new Dictionary<string, string>
                {
                    ["linkcss"] = "",
                };
                if (standalone)
                    attributes["toc"] = "auto";

                var parseOptions = new ParseOptions
                {
                    BaseDirectory = basePath,
                    IncludeMaxDepth = 10,
                    AllowUriRead = false,
                    // Same as export: includes may leave the document's folder.
                    SafeMode = SafeMode.Unsafe,
                    Attributes = attributes,
                };

                var parsed = AdocParser.Parse(source, parseOptions);

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

                var renderOptions = new HtmlRenderOptions
                {
                    FullDocument = standalone,
                    Theme = standalone ? HtmlTheme.Asciidoctor : HtmlTheme.None,
                };

                using var ms = new MemoryStream();
                _renderer.Render(parsed.Document, ms, renderOptions);
                var html = Encoding.UTF8.GetString(ms.ToArray());

                return RenderResult.Success(html, parsed.Document, parsed.LineOrigins, parsed.Diagnostics);
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
