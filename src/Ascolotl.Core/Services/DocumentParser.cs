using AdocNet.Importers;
using AdocNet.Parser;

namespace Ascolotl.Core.Services;

/// <summary>Parses a document with its format's parser, for the preview and every export.</summary>
public static class DocumentParser
{
    /// <param name="basePath">The folder AsciiDoc includes resolve against.</param>
    /// <param name="attributes">AsciiDoc attributes set from outside the document.</param>
    /// <param name="projectFolder">The folder open in the app, which includes may reach across.</param>
    public static ParseResult Parse(string source, bool markdown, string? basePath,
        IReadOnlyDictionary<string, string>? attributes = null, string? projectFolder = null)
        => markdown
            ? MarkdownParser.Parse(source)
            : AdocParser.Parse(source, LocalIncludeReader.ParseOptions(basePath, attributes, projectFolder));
}
