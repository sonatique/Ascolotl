using System.Text.RegularExpressions;

namespace Ascolotl.Core.Editing;

/// <summary>What a span of AsciiDoc source is, for colouring the editor.</summary>
public enum SyntaxKind
{
    Heading,
    Attribute,
    BlockTitle,
    Delimiter,
    Comment,
    ListMarker,
    Admonition,
    Macro,
    Monospace,
}

/// <summary>
/// Line-by-line AsciiDoc colouring for the source editor. Deliberately
/// shallow (one line at a time, no block state): it colours what the eye
/// scans for, and a miss only costs a colour, never the text.
/// </summary>
public static partial class AsciiDocSyntax
{
    public readonly record struct Span(int Start, int Length, SyntaxKind Kind);

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
        if (BlockTitle().IsMatch(line))
            return [new(0, line.Length, SyntaxKind.BlockTitle)];
        if (AttributeEntry().Match(line) is { Success: true } attr)
            return [new(0, attr.Groups[1].Length, SyntaxKind.Attribute)];

        var restStart = 0;
        if (ListMarker().Match(line) is { Success: true } marker)
        {
            spans.Add(new(marker.Groups[1].Index, marker.Groups[1].Length, SyntaxKind.ListMarker));
            restStart = marker.Length;
        }
        else if (AdmonitionLabel().Match(line) is { Success: true } admonition)
        {
            spans.Add(new(0, admonition.Groups[1].Length, SyntaxKind.Admonition));
            restStart = admonition.Length;
        }

        var rest = line[restStart..];
        foreach (Match m in Macro().Matches(rest))
            spans.Add(new(restStart + m.Index, m.Length, SyntaxKind.Macro));
        foreach (Match m in Monospace().Matches(rest))
            if (!spans.Any(s => Overlaps(s, restStart + m.Index, m.Length)))
                spans.Add(new(restStart + m.Index, m.Length, SyntaxKind.Monospace));

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    private static bool Overlaps(Span s, int start, int length)
        => start < s.Start + s.Length && s.Start < start + length;

    [GeneratedRegex(@"^//(?!/)")]
    private static partial Regex Comment();

    [GeneratedRegex(@"^={1,6}\s+\S")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(-{4,}|={4,}|\*{4,}|_{4,}|\+{4,}|\.{4,}|/{4,}|--|\|===|,===|:===|!===)\s*$")]
    private static partial Regex Delimiter();

    [GeneratedRegex(@"^\.[^.\s]")]
    private static partial Regex BlockTitle();

    // ":name:" (also ":name!:" / ":!name:"); the value after it stays plain.
    [GeneratedRegex(@"^(:!?[\w][\w-]*!?:)(\s|$)")]
    private static partial Regex AttributeEntry();

    [GeneratedRegex(@"^\s*([*.\-]+|\d+\.)\s+")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^((NOTE|TIP|IMPORTANT|WARNING|CAUTION):)\s")]
    private static partial Regex AdmonitionLabel();

    // link:/image:/include::/xref:… macros, bare URLs and <<cross references>>.
    [GeneratedRegex(@"\b(?:link|image|include|xref|mailto|footnote|kbd|btn|menu|pass|stem|latexmath|asciimath|video|audio|icon)::?[^\s\[]*\[[^\]]*\]|\bhttps?://[^\s\[\]<>]+(?:\[[^\]]*\])?|<<[^>]+>>")]
    private static partial Regex Macro();

    [GeneratedRegex(@"`[^`]+`")]
    private static partial Regex Monospace();
}
