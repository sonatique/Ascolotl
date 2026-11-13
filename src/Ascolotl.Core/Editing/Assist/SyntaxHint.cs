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
            return "Attribute reference — {name}";

        // Inside a table: advertise the cell/row shortcuts and where columns live.
        if (AsciiDocContextAnalyzer.IsInsideTable(text, caretOffset))
            return "Table — Tab: new cell · Enter: new row · right-click the preview to add/remove columns";

        if (trimmed.Length == 0)
            return null;

        // Headings: leading run of '='.
        if (trimmed[0] == '=')
        {
            var eq = 0;
            while (eq < trimmed.Length && trimmed[eq] == '=') eq++;
            if (eq < trimmed.Length && trimmed[eq] == ' ')
                return eq == 1 ? "Document title" : $"Section heading (level {eq - 1})";
        }

        if (StartsWithMarker(trimmed, '*') || StartsWithMarker(trimmed, '-'))
            return "Unordered list item";
        if (StartsWithMarker(trimmed, '.'))
            return "Ordered list item";

        foreach (var label in new[] { "NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION" })
            if (trimmed.StartsWith(label + ":", StringComparison.Ordinal))
                return $"Admonition ({label})";

        if (trimmed.StartsWith("[source", StringComparison.OrdinalIgnoreCase))
            return "Source block attributes — [source,language]";
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
            return "Block attribute list";
        if (trimmed is "|===" || trimmed.StartsWith("|==", StringComparison.Ordinal))
            return "Table delimiter";
        if (IsFence(trimmed, '-')) return "Listing block delimiter";
        if (IsFence(trimmed, '=')) return "Example block delimiter";
        if (IsFence(trimmed, '*')) return "Sidebar block delimiter";
        if (IsFence(trimmed, '_')) return "Quote block delimiter";
        if (IsFence(trimmed, '.')) return "Literal block delimiter";
        if (IsFence(trimmed, '+')) return "Passthrough block delimiter";
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return "Comment (not rendered)";
        if (trimmed.StartsWith("image::", StringComparison.OrdinalIgnoreCase))
            return "Block image — image::path[alt]";
        if (IsAttributeEntry(trimmed))
            return "Document attribute entry — :name: value";

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
