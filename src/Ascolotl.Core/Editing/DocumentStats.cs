namespace Ascolotl.Core.Editing;

/// <summary>Word/character stats for a document, plus estimated reading time.</summary>
/// <param name="Words">Prose word count (markup lines excluded).</param>
/// <param name="Characters">Total character count of the source.</param>
/// <param name="ReadingMinutes">Estimated reading time at ~200 wpm, minimum 1
/// when there is any prose.</param>
public readonly record struct DocumentStatsResult(int Words, int Characters, int ReadingMinutes);

/// <summary>
/// Counts the words a *reader* would read: AsciiDoc structural lines (comments,
/// attribute entries, block attribute lists, delimiter fences) are skipped, and
/// line markers (heading <c>=</c>, list bullets, table pipes) don't count as
/// words. Pure and line-based — cheap enough to run on every keystroke.
/// </summary>
public static class DocumentStats
{
    private const int WordsPerMinute = 200;

    public static DocumentStatsResult Count(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new DocumentStatsResult(0, 0, 0);

        var words = 0;
        var rest = text.AsSpan();
        while (true)
        {
            var nl = rest.IndexOf('\n');
            words += CountLineWords(nl < 0 ? rest : rest[..nl]);
            if (nl < 0) break;
            rest = rest[(nl + 1)..];
        }

        var minutes = words == 0
            ? 0
            : Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute, MidpointRounding.AwayFromZero));
        return new DocumentStatsResult(words, text.Length, minutes);
    }

    private static int CountLineWords(ReadOnlySpan<char> line)
    {
        var t = line.TrimStart();
        if (t.Length == 0) return 0;

        // Structural lines a reader never reads.
        if (t.StartsWith("//")) return 0;                                       // comment
        if (t[0] == '[' && t.TrimEnd().EndsWith(']')) return 0;                 // block attributes / anchor
        if (t[0] == ':' && t.Length > 2 && t[1] != ':' && t[2..].Contains(':')) return 0; // attribute entry
        if (IsDelimiter(t)) return 0;                                            // fences, |===, '''

        var words = 0;
        var inWord = false;
        foreach (var ch in t)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (!inWord) { words++; inWord = true; }
            }
            else if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            // punctuation/markup chars neither start nor split a word:
            // "*bold*" is one word, "don't" is one word.
        }
        return words;
    }

    private static bool IsDelimiter(ReadOnlySpan<char> t)
    {
        var s = t.TrimEnd();
        if (s.SequenceEqual("'''") || s.SequenceEqual("<<<")) return true;
        if (s.Length >= 3 && s[0] == '|' && s[1..].IndexOfAnyExcept('=') < 0) return true;
        if (s.Length < 4) return false;
        var c0 = s[0];
        return (c0 is '-' or '=' or '.' or '*' or '_' or '+') && s.IndexOfAnyExcept(c0) < 0;
    }
}
