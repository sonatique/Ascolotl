using AdocNet;
using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

/// <summary>
/// Translates AdocNet parse <see cref="Diagnostic"/>s into editor-anchored
/// <see cref="DocumentDiagnostic"/>s. As of AdocNet 1.0.17 (Adoc.Net#67),
/// diagnostic ranges are reported in <em>original source</em> coordinates, so
/// a main-document diagnostic's line is used directly (no line-origin
/// translation — doing so would now mis-map it).
///
/// <para>A diagnostic that originates inside an <c>include::</c>d file carries
/// that file's path in <see cref="Diagnostic.FilePath"/>, and its line is
/// relative to <em>that</em> file — not the main document the editor shows —
/// so it can't be anchored inline. Those (and rangeless diagnostics) map to
/// <c>Line == -1</c>: surfaced in the status bar but not squiggled. A
/// main-document diagnostic reports a null/empty <c>FilePath</c>.</para>
/// </summary>
public static class DiagnosticMapper
{
    public static List<DocumentDiagnostic> Map(IReadOnlyList<Diagnostic>? diagnostics)
    {
        var result = new List<DocumentDiagnostic>();
        if (diagnostics is null || diagnostics.Count == 0) return result;

        foreach (var d in diagnostics)
        {
            int line, startCol, endCol;
            string? sourceFile = null;
            var sourceFileLine = 0;

            if (d.Range.IsNone)
            {
                line = -1;
                startCol = endCol = 1;
            }
            else if (!string.IsNullOrEmpty(d.FilePath))
            {
                // Originates inside an included file: its line is relative to
                // that file, not the editor's main document, so it can't be
                // squiggled. Keep the file + line for the status-bar summary.
                line = -1;
                startCol = endCol = 1;
                sourceFile = Path.GetFileName(d.FilePath);
                sourceFileLine = d.Range.Start.Line;
            }
            else
            {
                line = d.Range.Start.Line - 1; // 0-based editor line (source coords)
                startCol = Math.Max(1, d.Range.Start.Column);
                endCol = d.Range.End.Line == d.Range.Start.Line
                    ? Math.Max(startCol, d.Range.End.Column)
                    : startCol; // multi-line: the renderer extends to the line
            }

            result.Add(new DocumentDiagnostic(
                line, startCol, endCol, Map(d.Severity), d.Message ?? string.Empty,
                sourceFile, sourceFileLine));
        }
        return result;
    }

    private static Models.DiagnosticSeverity Map(AdocNet.DiagnosticSeverity severity) => severity switch
    {
        AdocNet.DiagnosticSeverity.Error => Models.DiagnosticSeverity.Error,
        AdocNet.DiagnosticSeverity.Warning => Models.DiagnosticSeverity.Warning,
        _ => Models.DiagnosticSeverity.Info,
    };
}
