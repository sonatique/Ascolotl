namespace Ascolotl.Core.Models;

/// <summary>A format Import converts into a new AsciiDoc document.</summary>
public enum ImportFormat
{
    Word,
    PlainText,
}

public static class ImportFormatExtensions
{
    public static string DisplayName(this ImportFormat format) => format switch
    {
        ImportFormat.Word => "Word Document",
        ImportFormat.PlainText => "Plain Text",
        _ => format.ToString(),
    };

    public static IReadOnlyList<string> Patterns(this ImportFormat format) => format switch
    {
        ImportFormat.Word => ["*.docx"],
        ImportFormat.PlainText => ["*.txt", "*.text"],
        _ => ["*.*"],
    };
}
