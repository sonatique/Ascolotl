using System.Text.RegularExpressions;

namespace Ascolotl.Core.Editing.Assist;

/// <summary>How to continue a list when Enter is pressed on a list item.</summary>
/// <param name="Marker">The full prefix (indent + marker + space, plus a
/// checkbox stub for checklists) to start the next item with.</param>
/// <param name="TerminateEmpty">True when the current item is empty, signalling
/// the caller should drop the marker and end the list instead.</param>
public sealed record ListContinuation(string Marker, bool TerminateEmpty);

/// <summary>
/// Pure helpers for "smart typing" quality-of-life behaviors: continuing a list
/// on Enter, and auto-closing a delimited block. All decisions are made from the
/// text alone so they can be unit-tested without an editor.
/// </summary>
public static partial class SmartTyping
{
    [GeneratedRegex(@"^\[([ xX*])\]\s?(.*)$")]
    private static partial Regex CheckboxRegex();

    /// <summary>
    /// Given the text of the line up to the caret, decide how Enter should
    /// continue a list, or null when the line isn't a list item.
    /// </summary>
    public static ListContinuation? ContinueList(string linePrefix)
    {
        var i = 0;
        while (i < linePrefix.Length && (linePrefix[i] == ' ' || linePrefix[i] == '\t')) i++;
        var indent = linePrefix[..i];

        if (i >= linePrefix.Length) return null;
        var c = linePrefix[i];

        string marker;
        if (c is '*' or '.')
        {
            var j = i;
            while (j < linePrefix.Length && linePrefix[j] == c) j++;
            marker = linePrefix[i..j];
            i = j;
        }
        else if (c == '-')
        {
            marker = "-";
            i++;
        }
        else
        {
            return null;
        }

        // A real list marker is followed by a space.
        if (i >= linePrefix.Length || linePrefix[i] != ' ') return null;
        while (i < linePrefix.Length && linePrefix[i] == ' ') i++;

        var content = linePrefix[i..];
        var plainMarker = indent + marker + " ";

        // Checklist item, e.g. "* [ ] task".
        var box = CheckboxRegex().Match(content);
        if (box.Success)
        {
            var afterBox = box.Groups[2].Value;
            return afterBox.Trim().Length == 0
                ? new ListContinuation(plainMarker, TerminateEmpty: true)
                : new ListContinuation(indent + marker + " [ ] ", TerminateEmpty: false);
        }

        // Empty item → end the list.
        if (content.Trim().Length == 0)
            return new ListContinuation(plainMarker, TerminateEmpty: true);

        return new ListContinuation(plainMarker, TerminateEmpty: false);
    }

    /// <summary>
    /// When <paramref name="lineText"/> is exactly an opening block delimiter,
    /// return the matching closing fence to auto-insert; otherwise null.
    /// </summary>
    public static string? ClosingDelimiterFor(string lineText)
    {
        var t = lineText.Trim();
        if (t.Length == 0) return null;

        if (t is "|===" || TableDelimiter().IsMatch(t)) return "|===";

        // Symmetric fences: the closer is identical to the opener.
        if (ListingDelimiter().IsMatch(t)
            || LiteralDelimiter().IsMatch(t)
            || ExampleDelimiter().IsMatch(t)
            || SidebarDelimiter().IsMatch(t)
            || QuoteDelimiter().IsMatch(t)
            || PassthroughDelimiter().IsMatch(t))
            return t;

        return null;
    }

    [GeneratedRegex(@"^\|=+$")] private static partial Regex TableDelimiter();
    [GeneratedRegex(@"^-{4,}$")] private static partial Regex ListingDelimiter();
    [GeneratedRegex(@"^\.{4,}$")] private static partial Regex LiteralDelimiter();
    [GeneratedRegex(@"^={4,}$")] private static partial Regex ExampleDelimiter();
    [GeneratedRegex(@"^\*{4,}$")] private static partial Regex SidebarDelimiter();
    [GeneratedRegex(@"^_{4,}$")] private static partial Regex QuoteDelimiter();
    [GeneratedRegex(@"^\+{4,}$")] private static partial Regex PassthroughDelimiter();
}
