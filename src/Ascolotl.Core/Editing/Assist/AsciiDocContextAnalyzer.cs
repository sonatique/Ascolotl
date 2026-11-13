namespace Ascolotl.Core.Editing.Assist;

/// <summary>
/// Classifies the caret position into an <see cref="AssistQuery"/> using
/// line-oriented heuristics. AsciiDoc's block grammar is line-based, so a full
/// parse isn't needed to know whether the caret is at block level, inside a
/// block attribute list, in an attribute reference, or in inline prose.
/// </summary>
public static class AsciiDocContextAnalyzer
{
    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';

    public static AssistQuery Analyze(string? text, int caretOffset)
    {
        text ??= string.Empty;
        caretOffset = Math.Clamp(caretOffset, 0, text.Length);

        // Current line prefix (from line start up to the caret).
        var lineStart = caretOffset;
        while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;

        // No completion inside verbatim blocks (listing/literal/passthrough),
        // where the content is reproduced as-is and a tab is a real, meaningful
        // tab — not a completion trigger.
        if (IsInsideVerbatim(text, lineStart))
            return new AssistQuery(AssistContextKind.None, string.Empty, caretOffset, caretOffset, string.Empty);

        var prefix = text[lineStart..caretOffset];

        // Trailing identifier the user is typing.
        var w = prefix.Length;
        while (w > 0 && IsWordChar(prefix[w - 1])) w--;
        var word = prefix[w..];
        var replaceStart = lineStart + w;
        var lead = prefix[..w];

        // Attribute reference: the char immediately before the word is '{'.
        if (lead.EndsWith('{'))
            return new AssistQuery(AssistContextKind.AttributeReference, word, replaceStart, caretOffset, lead);

        // Cross-reference: the word follows "<<".
        if (lead.EndsWith("<<"))
            return new AssistQuery(AssistContextKind.CrossReference, word, replaceStart, caretOffset, lead);

        // Block attribute list: an unclosed '[' on this line, and only
        // whitespace before it (so we don't fire inside a macro's brackets like
        // link:url[…]).
        var open = prefix.LastIndexOf('[');
        var close = prefix.LastIndexOf(']');
        if (open >= 0 && open > close && prefix[..open].Trim().Length == 0)
            return new AssistQuery(AssistContextKind.AttributeList, word, replaceStart, caretOffset, lead);

        // Block level: nothing but whitespace before the word.
        if (lead.Trim().Length == 0)
            return new AssistQuery(AssistContextKind.BlockStart, word, replaceStart, caretOffset, lead);

        return new AssistQuery(AssistContextKind.Inline, word, replaceStart, caretOffset, lead);
    }

    /// <summary>
    /// True when <paramref name="caretLineStart"/> falls inside a verbatim
    /// delimited block — listing (<c>----</c>), literal (<c>....</c>) or
    /// passthrough (<c>++++</c>). Tables, examples, sidebars and quotes are NOT
    /// verbatim and are intentionally excluded (completion stays on there).
    /// </summary>
    private static bool IsInsideVerbatim(string text, int caretLineStart)
    {
        string? open = null;
        var i = 0;
        while (i < caretLineStart)
        {
            var end = i;
            while (end < text.Length && text[end] != '\n') end++;
            var line = text[i..end].Trim();

            if (open is null)
            {
                if (IsVerbatimFence(line)) open = line;
            }
            else if (line == open)
            {
                open = null;
            }

            i = end + 1;
        }
        return open is not null;
    }

    private static bool IsVerbatimFence(string trimmed)
    {
        if (trimmed.Length < 4) return false;
        var c = trimmed[0];
        if (c != '-' && c != '.' && c != '+') return false;
        foreach (var ch in trimmed)
            if (ch != c) return false;
        return true;
    }

    /// <summary>
    /// True when the caret sits between a table's <c>|===</c> delimiters. Used
    /// to drive table smart-typing (Tab → new cell, Enter → new row) and the
    /// status-bar hint.
    /// </summary>
    public static bool IsInsideTable(string? text, int caretOffset)
    {
        text ??= string.Empty;
        caretOffset = Math.Clamp(caretOffset, 0, text.Length);

        var caretLineStart = caretOffset;
        while (caretLineStart > 0 && text[caretLineStart - 1] != '\n') caretLineStart--;

        var inside = false;
        var i = 0;
        while (i < caretLineStart)
        {
            var end = i;
            while (end < text.Length && text[end] != '\n') end++;
            if (IsTableFence(text[i..end].Trim())) inside = !inside;
            i = end + 1;
        }
        return inside;
    }

    /// <summary>A table delimiter line: <c>|===</c> (or more '=').</summary>
    public static bool IsTableFence(string trimmed)
    {
        if (trimmed.Length < 4 || trimmed[0] != '|') return false;
        for (var i = 1; i < trimmed.Length; i++)
            if (trimmed[i] != '=') return false;
        return true;
    }
}
