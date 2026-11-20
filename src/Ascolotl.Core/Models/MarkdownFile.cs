namespace Ascolotl.Core.Models;

/// <summary>Markdown documents are told apart from AsciiDoc ones by their file extension.</summary>
public static class MarkdownFile
{
    public static IReadOnlyList<string> Extensions { get; } = [".md", ".markdown"];

    public static bool Is(string? path)
        => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
