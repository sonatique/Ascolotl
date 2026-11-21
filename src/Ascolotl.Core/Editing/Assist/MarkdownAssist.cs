using System.Text;
using System.Text.RegularExpressions;
using Ascolotl.Core.Resources;

namespace Ascolotl.Core.Editing.Assist;

/// <summary>
/// Completion and smart typing for Markdown, the counterpart of
/// <see cref="AsciiDocContextAnalyzer"/>, <see cref="AsciiDocAssistCatalog"/>
/// and <see cref="SmartTyping"/>. Markdown has little markup to complete, so
/// this offers what saves typing: block starters, code fence languages, link
/// targets (files, headings, reference labels), and list, quote and fence
/// continuation on Enter. Nothing is offered inside a fenced code block.
/// </summary>
public static partial class MarkdownAssist
{
    // ── Where the caret is ────────────────────────────────────────────────

    public static AssistQuery Analyze(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var prefix = text[lineStart..caret];
        AssistQuery None() => new(AssistContextKind.None, string.Empty, caret, caret, string.Empty);

        var fenceLine = Fence().Match(prefix);
        if (InsideFence(text, lineStart))
            return None();
        if (fenceLine.Success)
            return fenceLine.Groups[2].Value.Length == prefix.Length - fenceLine.Groups[2].Index
                ? Query(AssistContextKind.FenceLanguage, prefix, fenceLine.Groups[2].Index, lineStart, caret)
                : None();

        // A link target: after "](" with no ")" or space since.
        var target = prefix.LastIndexOf("](", StringComparison.Ordinal);
        if (target >= 0 && prefix.IndexOfAny([')', ' '], target) < 0)
        {
            var at = target + 2;
            return at < prefix.Length && prefix[at] == '#'
                ? Query(AssistContextKind.Anchor, prefix, at + 1, lineStart, caret)
                : Query(AssistContextKind.LinkTarget, prefix, at, lineStart, caret);
        }

        // A reference link's label: after "][" with no "]" since.
        var label = prefix.LastIndexOf("][", StringComparison.Ordinal);
        if (label >= 0 && prefix.IndexOf(']', label + 2) < 0)
            return Query(AssistContextKind.ReferenceLabel, prefix, label + 2, lineStart, caret);

        // A word at the start of the line: block starters.
        var w = prefix.Length;
        while (w > 0 && char.IsLetterOrDigit(prefix[w - 1])) w--;
        return prefix[..w].Trim().Length == 0
            ? Query(AssistContextKind.BlockStart, prefix, w, lineStart, caret)
            : None();
    }

    private static AssistQuery Query(AssistContextKind kind, string prefix, int wordAt, int lineStart, int caret)
        => new(kind, prefix[wordAt..], lineStart + wordAt, caret, prefix[..wordAt]);

    /// <summary>An odd number of fence lines above means the line is inside a code block.</summary>
    private static bool InsideFence(string text, int lineStart)
    {
        var inside = false;
        foreach (var line in text.AsSpan(0, lineStart).EnumerateLines())
            if (Fence().IsMatch(line)) inside = !inside;
        return inside;
    }

    // ── What to offer ─────────────────────────────────────────────────────

    /// <param name="text">The whole document (headings and labels come from it).</param>
    /// <param name="folder">The document's folder, for file targets; null for an unsaved one.</param>
    public static IReadOnlyList<AssistItem> PoolFor(AssistQuery query, string text, string? folder)
    {
        var closes = query.CaretOffset < text.Length ? text[query.CaretOffset] : '\0';
        return query.Kind switch
        {
            AssistContextKind.BlockStart => Blocks,
            AssistContextKind.FenceLanguage => [.. AsciiDocAssistCatalog.Languages.Select(l =>
                new AssistItem(l, l, l, string.Format(Strings.AssistSourceLanguage, l), AssistItemKind.Language))],
            AssistContextKind.Anchor => [.. Headings(text).Select(h =>
                new AssistItem(h.Slug, h.Title, h.Slug + (closes == ')' ? "" : ")"), "#" + h.Slug, AssistItemKind.Inline))],
            AssistContextKind.ReferenceLabel => [.. Labels(text).Select(l =>
                new AssistItem(l, l, l + (closes == ']' ? "" : "]"), "[" + l + "]", AssistItemKind.Inline))],
            AssistContextKind.LinkTarget => Files(folder, query.Word, Image(query.Lead), closes == ')'),
            _ => [],
        };
    }

    // Labels shared with the AsciiDoc catalog; the description shows the Markdown syntax.
    private static AssistItem[] Blocks =>
    [
        new("table", Strings.AssistBlockTableLabel, "| ${1:Column} | ${2:Column} |\n| --- | --- |\n| ${3:Cell} | ${4:Cell} |\n$0",
            "| a | b |", AssistItemKind.Block),
        new("code", Strings.AssistBlockSourceLabel, "```${1:text}\n${2:code}\n```\n$0", "```lang", AssistItemKind.Block),
        new("task", Strings.AssistBlockChecklistLabel, "- [ ] ${1:Task}$0", "- [ ] task", AssistItemKind.Block),
        new("ulist", Strings.AssistBlockUlistLabel, "- ${1:First item}\n- ${2:Second item}$0", "- item", AssistItemKind.Block),
        new("olist", Strings.AssistBlockOlistLabel, "1. ${1:First item}\n2. ${2:Second item}$0", "1. item", AssistItemKind.Block),
        new("quote", Strings.AssistBlockQuoteLabel, "> ${1:Quotation.}$0", "> text", AssistItemKind.Block),
        new("h2", Strings.AssistBlockH2Label, "## ${1:Heading}$0", "## Heading", AssistItemKind.Block),
        new("h3", Strings.AssistBlockH3Label, "### ${1:Heading}$0", "### Heading", AssistItemKind.Block),
        new("h4", Strings.AssistBlockH4Label, "#### ${1:Heading}$0", "#### Heading", AssistItemKind.Block),
        new("image", Strings.AssistBlockImageLabel, "![${1:alt}](${2:image.png})$0", "![alt](path)", AssistItemKind.Macro),
        new("link", Strings.AssistInlineLinkLabel, "[${1:text}](${2:url})$0", "[text](url)", AssistItemKind.Macro),
        new("rule", Strings.AssistBlockRuleLabel, "---\n$0", "---", AssistItemKind.Block),
        new("note", Strings.AssistBlockNoteLabel, "> [!NOTE]\n> ${1:Note.}$0", "> [!NOTE]", AssistItemKind.Admonition),
        new("tip", Strings.AssistBlockTipLabel, "> [!TIP]\n> ${1:Tip.}$0", "> [!TIP]", AssistItemKind.Admonition),
        new("important", Strings.AssistBlockImportantLabel, "> [!IMPORTANT]\n> ${1:Important.}$0", "> [!IMPORTANT]", AssistItemKind.Admonition),
        new("warning", Strings.AssistBlockWarningLabel, "> [!WARNING]\n> ${1:Warning.}$0", "> [!WARNING]", AssistItemKind.Admonition),
        new("caution", Strings.AssistBlockCautionLabel, "> [!CAUTION]\n> ${1:Caution.}$0", "> [!CAUTION]", AssistItemKind.Admonition),
    ];

    /// <summary>The document's ATX headings with GitHub's anchors (repeats get -1, -2…).</summary>
    public static IEnumerable<(string Title, string Slug)> Headings(string text)
    {
        var seen = new Dictionary<string, int>();
        var inFence = false;
        foreach (var line in text.Split('\n'))
        {
            if (Fence().IsMatch(line)) inFence = !inFence;
            if (inFence || Heading().Match(line) is not { Success: true } m) continue;
            var title = m.Groups[1].Value.Trim();
            var slug = Slug(title);
            var n = seen.GetValueOrDefault(slug);
            seen[slug] = n + 1;
            yield return (title, n == 0 ? slug : $"{slug}-{n}");
        }
    }

    private static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        return sb.ToString();
    }

    private static IEnumerable<string> Labels(string text)
        => LinkDefinition().Matches(text).Select(m => m.Groups[1].Value)
            .Where(l => !l.Contains('$')) // "$1" would read as a snippet field
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>The link being typed is an image: its "[" follows a "!".</summary>
    private static bool Image(string lead)
    {
        var open = lead.LastIndexOf('[', Math.Max(0, lead.Length - 3));
        return open > 0 && lead[open - 1] == '!';
    }

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp"];

    /// <summary>Entries of the folder the typed path points into: folders first, then files.</summary>
    private static List<AssistItem> Files(string? folder, string typed, bool images, bool closed)
    {
        var items = new List<AssistItem>();
        if (folder is null || Path.IsPathRooted(typed) || typed.Contains("://", StringComparison.Ordinal)) return items;
        var slash = typed.LastIndexOf('/');
        var dirPart = slash >= 0 ? typed[..(slash + 1)] : string.Empty;
        string dir;
        try
        {
            dir = Path.GetFullPath(Path.Combine(folder, dirPart));
            // Never list another computer's share: Windows would sign in to it.
            if (dir.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(dir)) return items;
            var entries = new DirectoryInfo(dir).EnumerateFileSystemInfos()
                .Where(e => !e.Name.StartsWith('.') && !e.Name.Contains('$'))
                .Take(500)
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                var name = e.Name.Replace(" ", "%20", StringComparison.Ordinal);
                if (e is DirectoryInfo)
                    items.Add(new AssistItem(dirPart + name + "/", e.Name + "/", dirPart + name + "/", dirPart + name + "/", AssistItemKind.Inline));
                else if (!images || ImageExtensions.Contains(e.Extension, StringComparer.OrdinalIgnoreCase))
                    items.Add(new AssistItem(dirPart + name, e.Name, dirPart + name + (closed ? "" : ")"), dirPart + name, AssistItemKind.Inline));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An unreadable folder offers nothing.
        }
        return items;
    }

    // ── Enter ─────────────────────────────────────────────────────────────

    /// <summary>How Enter continues the line up to the caret: the next list item or
    /// quote line, or null. An empty item or quote line ends it instead.</summary>
    public static ListContinuation? ContinueList(string linePrefix)
    {
        if (ListItem().Match(linePrefix) is not { Success: true } m) return null;
        var quote = m.Groups["quote"].Value;
        var indent = m.Groups["indent"].Value;
        var bullet = m.Groups["bullet"].Value;
        var number = m.Groups["number"].Value;
        var box = m.Groups["box"].Success;
        var content = m.Groups["content"].Value;
        if (bullet.Length == 0 && number.Length == 0 && quote.Length == 0) return null;

        string marker;
        if (number.Length > 0)
            marker = $"{(long.TryParse(number[..^1], out var n) ? n + 1 : 1)}{number[^1]} ";
        else if (bullet.Length > 0)
            marker = bullet + " ";
        else
            marker = string.Empty;
        if (box) marker += "[ ] ";

        var next = quote + indent + marker;
        return new ListContinuation(next, TerminateEmpty: content.Trim().Length == 0);
    }

    /// <summary>The fence that closes the one opened on <paramref name="line"/>, or null
    /// when the line isn't an opening fence (or its block is already closed).</summary>
    public static string? ClosingFenceFor(string line, string text)
    {
        if (Fence().Match(line) is not { Success: true } m) return null;
        var fences = text.Split('\n').Count(l => Fence().IsMatch(l));
        return fences % 2 == 1 ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})(\w*)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^ {0,3}\[([^\]]+)\]:", RegexOptions.Multiline)]
    private static partial Regex LinkDefinition();

    [GeneratedRegex(@"^(?<quote>(?:>\s?)*)(?<indent>[ \t]*)(?:(?<bullet>[-*+])|(?<number>\d{1,9}[.)]))?(?(bullet)\s|(?(number)\s|))(?<box>\[[ xX]\]\s?)?(?<content>.*)$")]
    private static partial Regex ListItem();
}
