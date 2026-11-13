namespace Ascolotl.Core.Models;

/// <summary>
/// Result of importing a Word document. Word carries things AsciiDoc has no
/// concept of (page geometry, text boxes, exact fonts), so an import is
/// lossy by nature — the counts and issues say how lossy, rather than
/// pretending otherwise.
/// </summary>
public sealed class DocxImportOutcome
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Where the AsciiDoc was written.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Fraction of content units mapped without loss (0–1).</summary>
    public double Fidelity { get; init; } = 1.0;

    public int Images { get; init; }
    public int Tables { get; init; }

    /// <summary>Human-readable notes about what was approximated or lost.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    public static DocxImportOutcome Failure(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };
}

/// <summary>Formats an import outcome for the status bar. Pure.</summary>
public static class DocxImportSummary
{
    /// <summary>
    /// One line describing the import: file name, fidelity when anything was
    /// lost, and the counts worth knowing (images land in a media folder next
    /// to the document, so they're worth calling out).
    /// </summary>
    public static string Describe(DocxImportOutcome outcome)
    {
        if (!outcome.IsSuccess)
            return outcome.ErrorMessage ?? "Word import failed";

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
