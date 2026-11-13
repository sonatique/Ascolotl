using AdocNet;
using AdocNet.Parser;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockRenderService : IRenderService
{
    /// <summary>Explicit result to return; when null, a realistic result is
    /// derived from the source (real parse → Document + LineOrigins) so
    /// AST-dependent features like the outline behave as in production.</summary>
    public RenderResult? NextResult { get; set; }

    public int RenderCalled { get; set; }
    public string? LastSource { get; private set; }

    public bool LastStandalone { get; private set; }

    public Task<RenderResult> RenderAsync(string source, string? basePath, bool standalone, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenderCalled++;
        LastSource = source;
        LastStandalone = standalone;

        if (NextResult is not null)
            return Task.FromResult(NextResult);

        var parsed = AdocParser.Parse(source, new ParseOptions { BaseDirectory = basePath });
        return Task.FromResult(RenderResult.Success("<p>mock</p>", parsed.Document, parsed.LineOrigins, parsed.Diagnostics));
    }
}
