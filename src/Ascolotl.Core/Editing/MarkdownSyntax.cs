using System.Text.RegularExpressions;
using static Ascolotl.Core.Editing.AsciiDocSyntax;

namespace Ascolotl.Core.Editing;

/// <summary>
/// Line-by-line Markdown colouring for the source editor, in the same shallow
/// style and palette as <see cref="AsciiDocSyntax"/>: a fence's content is
/// coloured as if it were prose.
/// </summary>
public static partial class MarkdownSyntax
{
    public static IReadOnlyList<Span> Classify(string line)
    {
        var spans = new List<Span>();
        if (line.Length == 0)
            return spans;

        // Whole-line constructs first; they take the line.
        if (Comment().IsMatch(line))
            return [new(0, line.Length, SyntaxKind.Comment)];
        if (Heading().IsMatch(line))
            return [new(0, line.Length, SyntaxKind.Heading)];
        if (Delimiter().IsMatch(line))
            return [new(0, line.Length, SyntaxKind.Delimiter)];
        if (LinkDefinition().IsMatch(line))
            return [new(0, line.Length, SyntaxKind.Macro)];

        var restStart = 0;
        if (Marker().Match(line) is { Success: true } marker)
        {
            spans.Add(new(marker.Groups[1].Index, marker.Groups[1].Length, SyntaxKind.ListMarker));
            restStart = marker.Length;
            if (Alert().Match(line, restStart) is { Success: true } alert)
            {
                spans.Add(new(alert.Index, alert.Length, SyntaxKind.Admonition));
                restStart = alert.Index + alert.Length;
            }
        }

        var rest = line[restStart..];
        foreach (Match m in Code().Matches(rest))
            spans.Add(new(restStart + m.Index, m.Length, SyntaxKind.Monospace));
        foreach (Match m in Link().Matches(rest))
            if (!spans.Any(s => Overlaps(s, restStart + m.Index, m.Length)))
                spans.Add(new(restStart + m.Index, m.Length, SyntaxKind.Macro));

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    private static bool Overlaps(Span s, int start, int length)
        => start < s.Start + s.Length && s.Start < start + length;

    [GeneratedRegex(@"^\s*<!--")]
    private static partial Regex Comment();

    [GeneratedRegex(@"^ {0,3}#{1,6}(\s|$)")]
    private static partial Regex Heading();

    // Code fences, thematic breaks and setext underlines, front matter fences,
    // and a table's delimiter row.
    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})|^ {0,3}([-*_=])(\s*\2){2,}\s*$|^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|?\s*$|^\s*\|\s*:?-+:?\s*\|\s*$")]
    private static partial Regex Delimiter();

    // [label]: url
    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s*\S")]
    private static partial Regex LinkDefinition();

    // Quote markers (">"), then a bullet or number with its task box.
    [GeneratedRegex(@"^\s*((?:>\s?)+(?:(?:[-*+]|\d{1,9}[.)])(?:\s+\[[ xX]\])?)?|(?:[-*+]|\d{1,9}[.)])(?:\s+\[[ xX]\])?)(\s+|$)")]
    private static partial Regex Marker();

    // GitHub alerts: > [!NOTE]
    [GeneratedRegex(@"\G\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]")]
    private static partial Regex Alert();

    [GeneratedRegex(@"(`+)[^`].*?(?<!`)\1(?!`)")]
    private static partial Regex Code();

    // [text](url), ![alt](src), [text][ref], <https://…> and bare URLs.
    [GeneratedRegex(@"!?\[[^\]]*\](?:\([^)]*\)|\[[^\]]*\])|<https?://[^>\s]+>|\bhttps?://[^\s<>()\[\]]*[^\s<>()\[\].,;:!?'""]")]
    private static partial Regex Link();
}
