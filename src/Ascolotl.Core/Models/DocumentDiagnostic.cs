using System.Globalization;
using System.Text.RegularExpressions;

namespace Ascolotl.Core.Models;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A parser diagnostic (from <c>ParseResult.Diagnostics</c>) translated into
/// editor coordinates for display. <see cref="Line"/> is 0-based; it is
/// <c>-1</c> when the diagnostic has no editor representation — either it has
/// no source range, or it originates inside an <c>include::</c>d file. For the
/// latter, <see cref="SourceFile"/>/<see cref="SourceFileLine"/> carry that
/// file's name and the 1-based line within it, so the status bar can still say
/// <c>where</c> the problem is even though it can't be squiggled in the editor.
/// </summary>
public sealed record DocumentDiagnostic(
    int Line,
    int StartColumn,
    int EndColumn,
    DiagnosticSeverity Severity,
    string Message,
    string? SourceFile = null,
    int SourceFileLine = 0)
{
    /// <summary>True when the diagnostic can't be anchored to an editor line.</summary>
    public bool IsUnanchored => Line < 0;

    // AdocNet's malformed-table warning carries the cell deficit in its prose,
    // e.g. "...expected 6 cell(s) but found 1...". We parse those two numbers to
    // turn the diagnostic into a concrete edit the user can act on.
    private static readonly Regex IncompleteRowCells =
        new(@"expected (\d+) cell\(s\) but found (\d+)", RegexOptions.Compiled);

    /// <summary>
    /// A plain-English, actionable suggestion derived from the raw parser
    /// message, or <c>null</c> when we have no specific advice to add. Today it
    /// covers the malformed-table "incomplete row" warning, translating the
    /// reported cell deficit into the exact edit (how many cells to add).
    /// </summary>
    public string? Hint
    {
        get
        {
            var m = IncompleteRowCells.Match(Message);
            if (!m.Success) return null;

            var expected = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var found = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var deficit = expected - found;
            if (deficit <= 0) return null;

            var cells = deficit == 1 ? "cell" : "cells";
            return $"This row fills {found} of the {expected} cells the grid needs here. " +
                   $"Add {deficit} more {cells} (type {deficit} more \"|\") to this row — " +
                   "or reduce an oversized rowspan (a \".N+\" cell) above it.";
        }
    }
}
