using System.Globalization;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;

namespace Ascolotl.Export;

/// <summary>
/// The title-page rules asciidoctor-pdf (and AdocNet's PDF renderer) apply,
/// for views that draw the title page themselves (the styled live preview).
/// </summary>
public static class TitlePageLayout
{
    /// <summary>asciidoctor-pdf's rule: a titled document with <c>:title-page:</c> or the book doctype.</summary>
    public static bool Wanted(DocumentNode document) =>
        document.Title is not null
        && !document.Attributes.ContainsKey("notitle")
        && (document.Attributes.ContainsKey("title-page")
            || (document.Attributes.TryGetValue("doctype", out var doctype) && doctype == "book"));

    /// <summary>Splits the doctitle at its last title separator (": " by default) into main title and subtitle.</summary>
    public static (string Main, string? Subtitle) Partition(string title, IReadOnlyDictionary<string, string> attributes)
    {
        var separator = (attributes.TryGetValue("title-separator", out var s) && s.Length > 0 ? s : ":") + " ";
        var at = title.LastIndexOf(separator, StringComparison.Ordinal);
        return at < 0 ? (title, null) : (title[..at], title[(at + separator.Length)..]);
    }

    /// <summary>The doctitle as one line, the way the PDF prints it without a title page.</summary>
    public static string PlainTitle(string title) => title.Replace(" +\n", " ").Replace("\n", " ");

    /// <summary>A title-page text block's lines: " +" ends a line.</summary>
    public static string TitlePageText(string text) => text.Replace(" +\n", "\n").Trim();

    /// <summary>The author line ("A, B"); null when the document names no author.</summary>
    public static string? AuthorLine(IReadOnlyDictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("author", out var author) || string.IsNullOrWhiteSpace(author))
            return null;
        var authors = new List<string> { author };
        for (int i = 2; attributes.TryGetValue($"author_{i}", out var more); i++)
            authors.Add(more);
        return string.Join(", ", authors);
    }

    /// <summary>The revision line: "Version 1.0, date: remark"; null when the document has none.</summary>
    public static string? RevisionLine(IReadOnlyDictionary<string, string> attributes)
    {
        var parts = new List<string>();
        if (attributes.TryGetValue("revnumber", out var rev) && rev.Length > 0)
        {
            var label = attributes.TryGetValue("version-label", out var vl) ? vl : "Version";
            parts.Add(label.Length > 0 ? $"{label} {rev}" : rev);
        }
        if (attributes.TryGetValue("revdate", out var date) && date.Length > 0)
            parts.Add(date);
        if (parts.Count == 0)
            return null;
        var line = string.Join(", ", parts);
        return attributes.TryGetValue("revremark", out var remark) && remark.Length > 0 ? $"{line}: {remark}" : line;
    }

    /// <summary>
    /// Points from the page top for a theme "top": points below the content
    /// top ("130"), % of the content height ("10%") or of the page ("10vh").
    /// </summary>
    public static double ResolveTop(string top, PdfRenderOptions o)
    {
        top = top.Trim();
        double contentHeight = o.PageHeight - o.MarginTop - o.MarginBottom;
        if (top.EndsWith("vh", StringComparison.Ordinal) && TryNumber(top[..^2], out var vh))
            return o.PageHeight * vh / 100;
        if (top.EndsWith('%') && TryNumber(top[..^1], out var pct))
            return o.MarginTop + contentHeight * pct / 100;
        return o.MarginTop + (Length(top) ?? 0);
    }

    /// <summary>A theme length in points: plain points, or pt/px/in/mm/cm.</summary>
    public static double? Length(string value)
    {
        value = value.Trim();
        foreach (var (unit, factor) in new[] { ("pt", 1.0), ("px", 0.75), ("in", 72.0), ("mm", 72 / 25.4), ("cm", 72 / 2.54) })
            if (value.EndsWith(unit, StringComparison.Ordinal) && TryNumber(value[..^unit.Length], out var n))
                return n * factor;
        return TryNumber(value, out var pts) ? pts : null;
    }

    private static bool TryNumber(string s, out double value)
        => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
