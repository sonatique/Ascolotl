using Ascolotl.Core.Resources;
namespace Ascolotl.Core.Editing.Assist;

/// <summary>
/// The F1 reference for Markdown documents. Labels reuse the AsciiDoc entries' translations;
/// the description is the Markdown itself, the same in every language.
/// </summary>
public static class MarkdownAssistCatalog
{
    public static IReadOnlyList<AssistItem> ReferenceItems =>
    [
        Item("h1", string.Format(Strings.FormatHeading, 1), "# ${1:Title}$0"),
        Item("h2", string.Format(Strings.FormatHeading, 2), "## ${1:Heading}$0"),
        Item("h3", string.Format(Strings.FormatHeading, 3), "### ${1:Heading}$0"),
        Item("bold", Strings.AssistInlineBoldLabel, "**${1:bold}**$0", AssistItemKind.Inline),
        Item("italic", Strings.AssistInlineItalicLabel, "*${1:italic}*$0", AssistItemKind.Inline),
        Item("mono", Strings.AssistInlineMonoLabel, "`${1:code}`$0", AssistItemKind.Inline),
        Item("link", Strings.AssistInlineLinkLabel, "[${1:text}](${2:https://})$0", AssistItemKind.Inline),
        Item("image", Strings.AssistBlockImageLabel, "![${1:alt}](${2:path})$0", AssistItemKind.Macro),
        Item("ulist", Strings.AssistBlockUlistLabel, "- ${1:First item}\n- ${2:Second item}$0"),
        Item("olist", Strings.AssistBlockOlistLabel, "1. ${1:First item}\n2. ${2:Second item}$0"),
        Item("checklist", Strings.AssistBlockChecklistLabel, "- [ ] ${1:First task}\n- [ ] ${2:Second task}$0"),
        Item("quote", Strings.AssistBlockQuoteLabel, "> ${1:Quotation.}$0"),
        Item("source", Strings.AssistBlockSourceLabel, "```${1:text}\n${2:code}\n```\n$0"),
        Item("table", Strings.AssistBlockTableLabel, "| ${1:Column} | ${2:Column} |\n| --- | --- |\n| ${3:Cell} | ${4:Cell} |\n$0"),
        Item("rule", Strings.AssistBlockRuleLabel, "---$0"),
        Item("comment", Strings.AssistBlockCommentLabel, "<!-- ${1:comment} -->$0"),
    ];

    private static AssistItem Item(string trigger, string label, string snippet, AssistItemKind kind = AssistItemKind.Block)
        => new(trigger, label, snippet, Snippet.Parse(snippet).Text.TrimEnd('\n').Replace("\n", "   "), kind);
}
