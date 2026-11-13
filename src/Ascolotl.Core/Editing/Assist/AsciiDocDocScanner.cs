using System.Text;
using System.Text.RegularExpressions;

namespace Ascolotl.Core.Editing.Assist;

/// <summary>Completions harvested from the current document itself.</summary>
public sealed record DocAssistContext(
    IReadOnlyList<AssistItem> Anchors,
    IReadOnlyList<AssistItem> Attributes)
{
    public static readonly DocAssistContext Empty = new([], []);
}

/// <summary>
/// Scans AsciiDoc source for the things only the document can tell us — anchor
/// targets (for cross-references) and the attributes it defines — so completion
/// can offer real ids and names rather than a fixed list. Pure and line-based;
/// no parser dependency.
/// </summary>
public static partial class AsciiDocDocScanner
{
    [GeneratedRegex(@"\[\[([\p{L}\p{N}_][\p{L}\p{N}_:.\-]*)(?:,\s*([^\]]*))?\]\]")]
    private static partial Regex BlockAnchorRegex();

    [GeneratedRegex(@"\[#([\p{L}\p{N}_][\p{L}\p{N}_:.\-]*)\]")]
    private static partial Regex ShorthandAnchorRegex();

    [GeneratedRegex(@"(?m)^=+\s+(.+?)\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"(?m)^:([\p{L}\p{N}_][\p{L}\p{N}_-]*)!?:")]
    private static partial Regex AttributeEntryRegex();

    public static DocAssistContext Scan(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return DocAssistContext.Empty;

        var anchors = new List<AssistItem>();
        var seenAnchors = new HashSet<string>(StringComparer.Ordinal);

        void AddAnchor(string id, string? label)
        {
            if (!seenAnchors.Add(id)) return;
            var display = string.IsNullOrWhiteSpace(label) ? id : $"{id} — {label.Trim()}";
            anchors.Add(new AssistItem(
                id, display, id + ">>$0",
                string.IsNullOrWhiteSpace(label) ? "Cross-reference target." : label!.Trim(),
                AssistItemKind.Macro));
        }

        foreach (Match m in BlockAnchorRegex().Matches(source))
            AddAnchor(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null);
        foreach (Match m in ShorthandAnchorRegex().Matches(source))
            AddAnchor(m.Groups[1].Value, null);
        foreach (Match m in HeadingRegex().Matches(source))
        {
            var title = m.Groups[1].Value.Trim();
            var id = SectionId(title);
            if (id is not null) AddAnchor(id, title);
        }

        var attributes = new List<AssistItem>();
        var seenAttrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttributeEntryRegex().Matches(source))
        {
            var name = m.Groups[1].Value;
            if (!seenAttrs.Add(name)) continue;
            attributes.Add(new AssistItem(
                name, name, name + "}",
                "Document attribute (defined in this document).",
                AssistItemKind.Attribute));
        }

        return new DocAssistContext(anchors, attributes);
    }

    /// <summary>
    /// The auto-generated id for a section title, following Asciidoctor's
    /// defaults (idprefix <c>_</c>, idseparator <c>_</c>): lowercase, runs of
    /// non-alphanumerics collapse to a single separator, trimmed, prefixed.
    /// Returns null when the title has no usable characters.
    /// </summary>
    public static string? SectionId(string title)
    {
        var sb = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }

        var core = sb.ToString().Trim('_');
        return core.Length == 0 ? null : "_" + core;
    }
}
