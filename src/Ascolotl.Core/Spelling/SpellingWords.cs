using System.Text.RegularExpressions;

namespace Ascolotl.Core.Spelling;

/// <summary>
/// The prose words of a document's source, the ones worth a spell check: not its code blocks,
/// comments, attribute entries and lines, URLs, inline code, macro targets, attribute references
/// or HTML, nor words with digits, underscores, capitals inside (camelCase) or all in capitals.
/// </summary>
// ponytail: a line scanner, not the parser: a construct it doesn't know is checked as prose.
public static partial class SpellingWords
{
    public static List<(int Start, int Length)> Find(string text, bool markdown)
    {
        var words = new List<(int, int)>();
        string? fence = null;      // the delimiter of the verbatim or comment block we're in
        var frontMatter = false;
        var lineStart = 0;
        for (var n = 0; lineStart <= text.Length; n++)
        {
            var end = text.IndexOf('\n', lineStart);
            if (end < 0) end = text.Length;
            var line = text[lineStart..end].TrimEnd('\r');
            var trimmed = line.Trim();

            if (fence is not null)
            {
                if (fence == "```" ? trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal) : trimmed == fence)
                    fence = null;
            }
            else if (frontMatter)
                frontMatter = trimmed != "---";
            else if (markdown && n == 0 && trimmed == "---")
                frontMatter = true;
            else if (Fence(trimmed, markdown) is { } opened)
                fence = opened;
            else if (!SkippedLine(line, trimmed, markdown))
                Tokenize(lineStart, Masked(line, markdown), words);

            lineStart = end + 1;
        }
        return words;
    }

    /// <summary>The words worth a check in text as shown, without markup: URLs and emails left out.</summary>
    public static List<(int Start, int Length)> InShownText(string text)
    {
        var chars = text.ToCharArray();
        foreach (var pattern in new[] { Url(), Email() })
            foreach (Match m in pattern.Matches(text))
                Array.Fill(chars, ' ', m.Index, m.Length);
        var words = new List<(int, int)>();
        Tokenize(0, new string(chars), words);
        return words;
    }

    /// <summary>A line opening a block whose content isn't prose: what closes it.</summary>
    private static string? Fence(string trimmed, bool markdown)
    {
        if (trimmed.StartsWith("```", StringComparison.Ordinal) || markdown && trimmed.StartsWith("~~~", StringComparison.Ordinal)) return "```";
        if (markdown) return null;
        return trimmed.Length >= 4 && trimmed[0] is '-' or '.' or '+' or '/' && trimmed.All(c => c == trimmed[0]) ? trimmed : null;
    }

    private static bool SkippedLine(string line, string trimmed, bool markdown)
    {
        if (trimmed.Length == 0) return true;
        if (markdown)
            return line.StartsWith('\t') || line.StartsWith("    ", StringComparison.Ordinal); // indented code
        return trimmed.StartsWith("//", StringComparison.Ordinal)             // a comment
            || trimmed[0] == ':' && AttributeEntry().IsMatch(trimmed)        // :name: value
            || trimmed[0] == '[' && trimmed[^1] == ']'                       // [source,python], [[id]]
            || BlockMacro().IsMatch(trimmed);                                // image::, include::
    }

    /// <summary>The line with what isn't prose blanked out, its length and places kept.</summary>
    private static string Masked(string line, bool markdown)
    {
        var chars = line.ToCharArray();
        void Blank(Regex pattern, int group = 0)
        {
            foreach (Match m in pattern.Matches(line))
            {
                var g = m.Groups[group];
                if (g.Success) Array.Fill(chars, ' ', g.Index, g.Length);
            }
        }
        Blank(Url());
        Blank(Email());
        Blank(InlineCode());
        if (markdown)
        {
            Blank(Html()); // in AsciiDoc, <<id,text>> is a cross reference
            Blank(MarkdownLinkTarget());
        }
        else
        {
            Blank(WholeMacro());          // kbd:[Ctrl+C], pass:[…], stem:[…]
            Blank(MacroTarget());         // link:target[ and xref:id[: the text in [] stays
            Blank(AttributeReference());  // {name}
            Blank(Anchor());              // [[id]], [#id]
            Blank(CrossReference(), 1);   // <<id, of <<id,text>>
            Blank(Passthrough());         // +text+
        }
        return new string(chars);
    }

    private static void Tokenize(int lineStart, string masked, List<(int, int)> words)
    {
        for (var i = 0; i < masked.Length;)
        {
            if (!IsWordChar(masked[i])) { i++; continue; }
            var start = i;
            while (i < masked.Length && (IsWordChar(masked[i]) || masked[i] is '\'' or '’' && i + 1 < masked.Length && char.IsLetter(masked[i + 1]) && i > start))
                i++;
            var word = masked.AsSpan(start, i - start);
            if (Checkable(word)) words.Add((lineStart + start, i - start));
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool Checkable(ReadOnlySpan<char> word)
    {
        if (word.Length < 2) return false;
        var upper = 0;
        foreach (var c in word)
        {
            if (char.IsDigit(c) || c == '_') return false;
            if (char.IsUpper(c)) upper++;
        }
        if (upper == word.Length) return false;                        // NOTE, HTML
        return !(upper > 1 || upper == 1 && !char.IsUpper(word[0]));   // camelCase, iPhone
    }

    [GeneratedRegex(@"^:!?[\w-]+!?:")] private static partial Regex AttributeEntry();
    [GeneratedRegex(@"^(image|include|video|audio|toc|ifdef|ifndef|ifeval|endif)::")] private static partial Regex BlockMacro();
    [GeneratedRegex(@"\b(?:https?|ftp|file|mailto|irc):[^\s\[\]<>()]+|\bwww\.[^\s\[\]<>()]+", RegexOptions.IgnoreCase)] private static partial Regex Url();
    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")] private static partial Regex Email();
    [GeneratedRegex(@"`+[^`]*`+")] private static partial Regex InlineCode();
    [GeneratedRegex(@"</?[A-Za-z][^>]*>|<!--.*?-->")] private static partial Regex Html();
    [GeneratedRegex(@"\]\([^)]*\)|\]\[[^\]]*\]|<[a-z]+:[^>]+>")] private static partial Regex MarkdownLinkTarget();
    [GeneratedRegex(@"\b(?:kbd|btn|menu|pass|stem|latexmath|asciimath|indexterm2?|anchor):[^\[\s]*\[[^\]]*\]")] private static partial Regex WholeMacro();
    [GeneratedRegex(@"\b[a-z][\w-]*:{1,2}[^\s\[]*\[|\]")] private static partial Regex MacroTarget();
    [GeneratedRegex(@"\{[\w-]+\}")] private static partial Regex AttributeReference();
    [GeneratedRegex(@"\[\[[^\]]*\]\]|\[#[^\]]*\]")] private static partial Regex Anchor();
    [GeneratedRegex(@"(<<[^,>]*,?)[^>]*>>")] private static partial Regex CrossReference();
    [GeneratedRegex(@"\+\+?[^+]+\+\+?")] private static partial Regex Passthrough();
}
