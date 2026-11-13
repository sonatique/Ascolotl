using AdocNet;
using AdocNet.Ast;

namespace Ascolotl.Core.Models;

public sealed class RenderResult
{
    public bool IsSuccess { get; }
    public string HtmlContent { get; }
    public DocumentNode? Document { get; }

    /// <summary>Per-line provenance for the parsed document (AdocNet 1.0.13,
    /// #46): index = post-include-expansion line, value = origin file + line,
    /// with <c>IsSynthetic</c> marking content pulled in from includes.</summary>
    public IReadOnlyList<LineOrigin>? LineOrigins { get; }

    /// <summary>Parser diagnostics (warnings/errors) for the document, in AST
    /// coordinates. Mapped to editor lines by <see cref="Services.DiagnosticMapper"/>.</summary>
    public IReadOnlyList<Diagnostic>? Diagnostics { get; }

    public string? ErrorSummary { get; }

    private RenderResult(bool isSuccess, string htmlContent, DocumentNode? document,
        IReadOnlyList<LineOrigin>? lineOrigins, IReadOnlyList<Diagnostic>? diagnostics, string? errorSummary)
    {
        IsSuccess = isSuccess;
        HtmlContent = htmlContent;
        Document = document;
        LineOrigins = lineOrigins;
        Diagnostics = diagnostics;
        ErrorSummary = errorSummary;
    }

    public static RenderResult Success(string html, DocumentNode? document = null,
        IReadOnlyList<LineOrigin>? lineOrigins = null, IReadOnlyList<Diagnostic>? diagnostics = null)
        => new(true, html, document, lineOrigins, diagnostics, null);

    public static RenderResult Failure(string errorSummary)
        => new(false, string.Empty, null, null, null, errorSummary);
}
