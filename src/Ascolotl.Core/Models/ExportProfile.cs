namespace Ascolotl.Core.Models;

/// <summary>
/// A reusable, named export configuration — the unit of single-source
/// publishing. Pick a profile, get the right format in the right place, no
/// dialog dance.
/// </summary>
public sealed class ExportProfile
{
    public string Name { get; set; } = "New profile";

    public ExportFormat Format { get; set; } = ExportFormat.Pdf;

    /// <summary>Visual theme applied consistently to HTML and PDF output.</summary>
    public ExportTheme Theme { get; set; } = ExportTheme.Asciidoctor;

    /// <summary>Destination folder; null means "next to the source document".</summary>
    public string? OutputFolder { get; set; }

    /// <summary>
    /// Where this profile writes <paramref name="documentFilePath"/> /
    /// <paramref name="displayName"/>: profile folder (or the document's own
    /// folder), document base name, format extension. Null when no folder can
    /// be determined (unsaved document and no profile folder) — the caller
    /// falls back to a save dialog.
    /// </summary>
    public string? ResolveOutputPath(string? documentFilePath, string displayName)
    {
        var baseName = Path.GetFileNameWithoutExtension(
            documentFilePath is not null ? Path.GetFileName(documentFilePath) : displayName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "document";

        var folder = OutputFolder;
        if (string.IsNullOrEmpty(folder) && documentFilePath is not null)
            folder = Path.GetDirectoryName(documentFilePath);
        if (string.IsNullOrEmpty(folder))
            return null;

        return Path.Combine(folder, $"{baseName}.{Format.Extension()}");
    }
}
