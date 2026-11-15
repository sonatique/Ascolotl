namespace Ascolotl.Core.Models;

/// <summary>
/// Result of importing a Word, Markdown, HTML or PDF file. These carry things AsciiDoc has no
/// concept of (page geometry, text boxes, exact fonts), so an import is
/// lossy by nature — the counts and issues say how lossy, rather than
/// pretending otherwise.
/// </summary>
public sealed class ImportOutcome
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The unexpected exception behind a failure, kept for the log.</summary>
    public Exception? Exception { get; init; }

    /// <summary>Where the AsciiDoc was written.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Fraction of content units mapped without loss (0–1).</summary>
    public double Fidelity { get; init; } = 1.0;

    public int Images { get; init; }
    public int Tables { get; init; }

    /// <summary>Human-readable notes about what was approximated or lost.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    public static ImportOutcome Failure(string message, Exception? exception = null) =>
        new() { IsSuccess = false, ErrorMessage = message, Exception = exception };
}

/// <summary>Formats an import outcome for the status bar. Pure.</summary>
public static class ImportSummary
{
    /// <summary>
    /// One line describing the import: file name, fidelity when anything was
    /// lost, and the counts worth knowing (images land in a media folder next
    /// to the document, so they're worth calling out).
    /// </summary>
    public static string Describe(ImportOutcome outcome)
    {
        if (!outcome.IsSuccess)
            return outcome.ErrorMessage ?? "Import failed";

        var name = outcome.OutputPath is null
            ? "document"
            : Path.GetFileName(outcome.OutputPath);

        var parts = new List<string>();

        // Round DOWN so a near-perfect import never claims a flat 100%.
        var percent = (int)Math.Floor(Math.Clamp(outcome.Fidelity, 0, 1) * 100);
        parts.Add(percent >= 100 ? "fully mapped" : $"{percent}% mapped");

        if (outcome.Images > 0)
            parts.Add(outcome.Images == 1 ? "1 image" : $"{outcome.Images} images");
        if (outcome.Tables > 0)
            parts.Add(outcome.Tables == 1 ? "1 table" : $"{outcome.Tables} tables");
        if (outcome.Issues.Count > 0)
            parts.Add(outcome.Issues.Count == 1 ? "1 note" : $"{outcome.Issues.Count} notes");

        return $"Imported {name} — {string.Join(", ", parts)}";
    }
}
