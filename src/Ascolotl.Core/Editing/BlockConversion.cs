using System.Text.RegularExpressions;

namespace Ascolotl.Core.Editing;

/// <summary>What a block can be turned into, keeping its words.</summary>
public enum BlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    BulletedList,
    NumberedList,
    Quote,
    CodeBlock,
    Note,
}

/// <summary>
/// A block's source as another kind of block: its words kept, its markers (heading, list,
/// quote, note label, delimiters) swapped for the new kind's.
/// </summary>
public static partial class BlockConversion
{
    /// <summary>The note is AsciiDoc only.</summary>
    public static bool Supports(BlockKind kind, bool markdown) => !(markdown && kind == BlockKind.Note);

    public static string TurnInto(string block, BlockKind kind, bool markdown)
    {
        var nl = block.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = Words(block);
        var text = string.Join(nl, lines);
        return kind switch
        {
            BlockKind.Paragraph => text,
            >= BlockKind.Heading1 and <= BlockKind.Heading6 =>
                new string(markdown ? '#' : '=', kind - BlockKind.Heading1 + 1) + " " + string.Join(" ", lines),
            BlockKind.BulletedList => string.Join(nl, lines.Select(l => (markdown ? "- " : "* ") + l)),
            BlockKind.NumberedList => string.Join(nl, lines.Select((l, i) => (markdown ? $"{i + 1}. " : ". ") + l)),
            BlockKind.Quote => markdown ? string.Join(nl, lines.Select(l => "> " + l)) : $"____{nl}{text}{nl}____",
            BlockKind.CodeBlock => markdown ? $"```{nl}{text}{nl}```" : $"[source]{nl}----{nl}{text}{nl}----",
            BlockKind.Note => lines.Count == 1 ? "NOTE: " + text : $"[NOTE]{nl}===={nl}{text}{nl}====",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>The block's lines of words: inside its delimiters, without a style line above
    /// them, without heading, list, quote or note markers, without blank lines.</summary>
    private static List<string> Words(string block)
    {
        var lines = block.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && StyleLine().IsMatch(lines[0].Trim())) lines.RemoveAt(0);
        var verbatim = false;
        if (lines.Count >= 2 && Delimiter().IsMatch(lines[0].Trim()) && Delimiter().IsMatch(lines[^1].Trim()))
        {
            verbatim = lines[0].TrimStart()[0] is '-' or '.' or '+' or '`'; // code keeps its leading - and *
            lines = lines[1..^1];
        }
        return [.. lines.Select(l => verbatim ? l.TrimEnd() : Marker().Replace(l, "").Trim()).Where(l => l.Trim().Length > 0)];
    }

    // [source,python,start=3], [quote], [NOTE]: a block's style; not an [[id]].
    [GeneratedRegex(@"^\[(?!\[).*\]$")]
    private static partial Regex StyleLine();

    [GeneratedRegex(@"^(?:-{4,}|\.{4,}|={4,}|\*{4,}|_{4,}|\+{4,}|```[\w+#.-]*)$")]
    private static partial Regex Delimiter();

    // A heading's = or #, a list's * - + . or 1., a quote's >, a note's label.
    [GeneratedRegex(@"^\s*(?:(?:=+|#{1,6})\s+|(?:[*\-+]+|\.+|\d{1,9}[.)])\s+|>\s?|(?:NOTE|TIP|IMPORTANT|WARNING|CAUTION):\s+)")]
    private static partial Regex Marker();
}
