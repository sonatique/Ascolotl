using Ascolotl.Core.Resources;
namespace Ascolotl.Core.Editing.Assist;

/// <summary>
/// Produces a one-line, plain-English description of the AsciiDoc syntax under
/// the caret, for the status bar. Returns null when there's nothing useful to
/// say (ordinary prose).
/// </summary>
public static class SyntaxHint
{
    public static string? Describe(string? text, int caretOffset)
    {
        text ??= string.Empty;
        caretOffset = Math.Clamp(caretOffset, 0, text.Length);

        var lineStart = caretOffset;
        while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
        var lineEnd = caretOffset;
        while (lineEnd < text.Length && text[lineEnd] != '\n') lineEnd++;
        var line = text[lineStart..lineEnd];
        var trimmed = line.TrimStart();

        // Attribute reference takes priority — it can appear mid-line.
        var query = AsciiDocContextAnalyzer.Analyze(text, caretOffset);
        if (query.Kind == AssistContextKind.AttributeReference)
            return Strings.HintAttributeReference;

        // Inside a table: advertise the cell/row shortcuts and where columns live.
        if (AsciiDocContextAnalyzer.IsInsideTable(text, caretOffset))
            return Strings.HintTable;

        if (trimmed.Length == 0)
            return null;

        // Headings: leading run of '='.
        if (trimmed[0] == '=')
        {
            var eq = 0;
            while (eq < trimmed.Length && trimmed[eq] == '=') eq++;
            if (eq < trimmed.Length && trimmed[eq] == ' ')
                return eq == 1 ? Strings.HintDocumentTitle : string.Format(Strings.HintSectionHeading, eq - 1);
        }

        if (StartsWithMarker(trimmed, '*') || StartsWithMarker(trimmed, '-'))
            return Strings.HintUnorderedListItem;
        if (StartsWithMarker(trimmed, '.'))
            return Strings.HintOrderedListItem;

        foreach (var label in new[] { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" })
            if (trimmed.StartsWith(label + ":", StringComparison.Ordinal))
                return string.Format(Strings.HintAdmonition, label);

        if (trimmed.StartsWith("[source", StringComparison.OrdinalIgnoreCase))
            return Strings.HintSourceAttributes;
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
            return Strings.HintBlockAttributeList;
        if (trimmed is "|===" || trimmed.StartsWith("|==", StringComparison.Ordinal))
            return Strings.HintTableDelimiter;
        if (IsFence(trimmed, '-')) return Strings.HintListingDelimiter;
        if (IsFence(trimmed, '=')) return Strings.HintExampleDelimiter;
        if (IsFence(trimmed, '*')) return Strings.HintSidebarDelimiter;
        if (IsFence(trimmed, '_')) return Strings.HintQuoteDelimiter;
        if (IsFence(trimmed, '.')) return Strings.HintLiteralDelimiter;
        if (IsFence(trimmed, '+')) return Strings.HintPassthroughDelimiter;
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return Strings.HintComment;
        if (trimmed.StartsWith("image::", StringComparison.OrdinalIgnoreCase))
            return Strings.HintBlockImage;
        if (IsAttributeEntry(trimmed))
            return Strings.HintAttributeEntry;

        return null;
    }

    private static bool StartsWithMarker(string s, char marker)
    {
        var i = 0;
        while (i < s.Length && s[i] == marker) i++;
        return i > 0 && i < s.Length && s[i] == ' ';
    }

    private static bool IsFence(string s, char c)
    {
        if (s.Length < 4) return false;
        foreach (var ch in s)
            if (ch != c) return false;
        return true;
    }

    private static bool IsAttributeEntry(string s)
    {
        if (s.Length < 3 || s[0] != ':') return false;
        var close = s.IndexOf(':', 1);
        return close > 1;
    }
}
