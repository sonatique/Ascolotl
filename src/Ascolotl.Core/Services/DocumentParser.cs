using AdocNet;
using AdocNet.Importers;
using AdocNet.Parser;

namespace Ascolotl.Core.Services;

/// <summary>Parses a document with its format's parser, for the preview and every export.</summary>
public static class DocumentParser
{
    /// <param name="basePath">The folder AsciiDoc includes resolve against.</param>
    /// <param name="attributes">AsciiDoc attributes set from outside the document.</param>
    public static ParseResult Parse(string source, bool markdown, string? basePath,
        IReadOnlyDictionary<string, string>? attributes = null)
        => markdown
            ? WithLineOrigins(MarkdownParser.Parse(source), source)
            : AdocParser.Parse(source, LocalIncludeReader.ParseOptions(basePath, attributes));

    // ponytail: AdocNet 1.0.41's MarkdownParser leaves LineOrigins empty, which hides
    // every block from the outline and the preview's block commands. Markdown has no
    // includes, so each line is its own origin. Drop once MarkdownParser fills them.
    private static ParseResult WithLineOrigins(ParseResult parsed, string source)
    {
        if (parsed.LineOrigins is { Count: > 0 }) return parsed;
        var lines = source.Count(c => c == '\n') + 1;
        return parsed with { LineOrigins = [.. Enumerable.Range(1, lines).Select(n => new LineOrigin(null!, n, false))] };
    }
}
