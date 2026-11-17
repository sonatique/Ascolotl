using Ascolotl.Core.Resources;
namespace Ascolotl.Core.Editing.Assist;

/// <summary>
/// The hand-authored repository of AsciiDoc completions. AdocNet exposes no
/// syntax metadata, so the item set lives here. <see cref="PoolFor"/> returns
/// every candidate for a context (the UI lets AvaloniaEdit filter live as the
/// user types); <see cref="ItemsFor"/> applies the prefix filter itself (used by
/// tests and any non-live caller).
/// </summary>
public static class AsciiDocAssistCatalog
{
    // --- Block-level scaffolds (line start) ---
    private static readonly AssistItem[] Blocks =
    {
        new("note", Strings.AssistBlockNoteLabel, "NOTE: ${1:Important note.}$0",
            Strings.AssistBlockNoteDesc, AssistItemKind.Admonition),
        new("tip", Strings.AssistBlockTipLabel, "TIP: ${1:Helpful tip.}$0",
            Strings.AssistBlockTipDesc, AssistItemKind.Admonition),
        new("important", Strings.AssistBlockImportantLabel, "IMPORTANT: ${1:Take note.}$0",
            Strings.AssistBlockImportantDesc, AssistItemKind.Admonition),
        new("warning", Strings.AssistBlockWarningLabel, "WARNING: ${1:Be careful.}$0",
            Strings.AssistBlockWarningDesc, AssistItemKind.Admonition),
        new("caution", Strings.AssistBlockCautionLabel, "CAUTION: ${1:Proceed carefully.}$0",
            Strings.AssistBlockCautionDesc, AssistItemKind.Admonition),
        new("source", Strings.AssistBlockSourceLabel, "[source,${1:text}]\n----\n${2:code}\n----\n$0",
            Strings.AssistBlockSourceDesc, AssistItemKind.Block),
        new("listing", Strings.AssistBlockListingLabel, "----\n${1:text}\n----\n$0",
            Strings.AssistBlockListingDesc, AssistItemKind.Block),
        new("example", Strings.AssistBlockExampleLabel, "====\n${1:content}\n====\n$0",
            Strings.AssistBlockExampleDesc, AssistItemKind.Block),
        new("sidebar", Strings.AssistBlockSidebarLabel, "****\n${1:content}\n****\n$0",
            Strings.AssistBlockSidebarDesc, AssistItemKind.Block),
        new("quote", Strings.AssistBlockQuoteLabel, "[quote,${1:author},${2:source}]\n____\n${3:Quotation.}\n____\n$0",
            Strings.AssistBlockQuoteDesc, AssistItemKind.Block),
        new("table", Strings.AssistBlockTableLabel, "[cols=\"${1:1,1}\"]\n|===\n| ${2:Cell} | ${3:Cell}\n\n| ${4:Cell} | ${5:Cell}\n|===\n$0",
            Strings.AssistBlockTableDesc, AssistItemKind.Block),
        new("image", Strings.AssistBlockImageLabel, "image::${1:path}[${2:alt}]$0",
            Strings.AssistBlockImageDesc, AssistItemKind.Macro),
        new("ulist", Strings.AssistBlockUlistLabel, "* ${1:First item}\n* ${2:Second item}$0",
            Strings.AssistBlockUlistDesc, AssistItemKind.Block),
        new("olist", Strings.AssistBlockOlistLabel, ". ${1:First item}\n. ${2:Second item}$0",
            Strings.AssistBlockOlistDesc, AssistItemKind.Block),
        new("h2", Strings.AssistBlockH2Label, "== ${1:Heading}$0",
            Strings.AssistBlockH2Desc, AssistItemKind.Block),
        new("h3", Strings.AssistBlockH3Label, "=== ${1:Heading}$0",
            Strings.AssistBlockH3Desc, AssistItemKind.Block),
        new("h4", Strings.AssistBlockH4Label, "==== ${1:Heading}$0",
            Strings.AssistBlockH4Desc, AssistItemKind.Block),
        new("comment", Strings.AssistBlockCommentLabel, "// ${1:comment}$0",
            Strings.AssistBlockCommentDesc, AssistItemKind.Block),
        new("attribute", Strings.AssistBlockAttributeLabel, ":${1:name}: ${2:value}$0",
            Strings.AssistBlockAttributeDesc, AssistItemKind.Attribute),
        new("toc", Strings.AssistBlockTocLabel, ":toc:$0",
            Strings.AssistBlockTocDesc, AssistItemKind.Attribute),
        new("h5", Strings.AssistBlockH5Label, "===== ${1:Heading}$0",
            Strings.AssistBlockH5Desc, AssistItemKind.Block),
        new("admonitionblock", Strings.AssistBlockAdmonitionblockLabel, "[${1:NOTE}]\n====\n${2:content}\n====\n$0",
            Strings.AssistBlockAdmonitionblockDesc, AssistItemKind.Admonition),
        new("include", Strings.AssistBlockIncludeLabel, "include::${1:file.adoc}[]$0",
            Strings.AssistBlockIncludeDesc, AssistItemKind.Macro),
        new("ifdef", Strings.AssistBlockIfdefLabel, "ifdef::${1:attribute}[]\n${2:content}\nendif::[]\n$0",
            Strings.AssistBlockIfdefDesc, AssistItemKind.Macro),
        new("ifndef", Strings.AssistBlockIfndefLabel, "ifndef::${1:attribute}[]\n${2:content}\nendif::[]\n$0",
            Strings.AssistBlockIfndefDesc, AssistItemKind.Macro),
        new("ifeval", Strings.AssistBlockIfevalLabel, "ifeval::[${1:condition}]\n${2:content}\nendif::[]\n$0",
            Strings.AssistBlockIfevalDesc, AssistItemKind.Macro),
        new("anchor", Strings.AssistBlockAnchorLabel, "[[${1:id}]]$0",
            Strings.AssistBlockAnchorDesc, AssistItemKind.Block),
        new("title", Strings.AssistBlockTitleLabel, ".${1:Title}$0",
            Strings.AssistBlockTitleDesc, AssistItemKind.Block),
        new("dlist", Strings.AssistBlockDlistLabel, "${1:Term}:: ${2:Definition}$0",
            Strings.AssistBlockDlistDesc, AssistItemKind.Block),
        new("checklist", Strings.AssistBlockChecklistLabel, "* [ ] ${1:First task}\n* [ ] ${2:Second task}$0",
            Strings.AssistBlockChecklistDesc, AssistItemKind.Block),
        new("literal", Strings.AssistBlockLiteralLabel, "....\n${1:text}\n....\n$0",
            Strings.AssistBlockLiteralDesc, AssistItemKind.Block),
        new("verse", Strings.AssistBlockVerseLabel, "[verse,${1:author}]\n____\n${2:lines}\n____\n$0",
            Strings.AssistBlockVerseDesc, AssistItemKind.Block),
        new("open", Strings.AssistBlockOpenLabel, "--\n${1:content}\n--\n$0",
            Strings.AssistBlockOpenDesc, AssistItemKind.Block),
        new("passblock", Strings.AssistBlockPassblockLabel, "++++\n${1:raw}\n++++\n$0",
            Strings.AssistBlockPassblockDesc, AssistItemKind.Block),
        new("stemblock", Strings.AssistBlockStemblockLabel, "[stem]\n++++\n${1:equation}\n++++\n$0",
            Strings.AssistBlockStemblockDesc, AssistItemKind.Block),
        new("pagebreak", Strings.AssistBlockPagebreakLabel, "<<<$0",
            Strings.AssistBlockPagebreakDesc, AssistItemKind.Block),
        new("rule", Strings.AssistBlockRuleLabel, "'''$0",
            Strings.AssistBlockRuleDesc, AssistItemKind.Block),
        new("video", Strings.AssistBlockVideoLabel, "video::${1:url}[]$0",
            Strings.AssistBlockVideoDesc, AssistItemKind.Macro),
        new("audio", Strings.AssistBlockAudioLabel, "audio::${1:url}[]$0",
            Strings.AssistBlockAudioDesc, AssistItemKind.Macro),
    };

    // --- Inline macros and markup (mid-line) ---
    private static readonly AssistItem[] Inlines =
    {
        new("xref", Strings.AssistInlineXrefLabel, "xref:${1:id}[${2:text}]$0",
            Strings.AssistInlineXrefDesc, AssistItemKind.Macro),
        new("link", Strings.AssistInlineLinkLabel, "link:${1:https://}[${2:text}]$0",
            Strings.AssistInlineLinkDesc, AssistItemKind.Macro),
        new("image", Strings.AssistInlineImageLabel, "image:${1:path}[${2:alt}]$0",
            Strings.AssistInlineImageDesc, AssistItemKind.Macro),
        new("footnote", Strings.AssistInlineFootnoteLabel, "footnote:[${1:text}]$0",
            Strings.AssistInlineFootnoteDesc, AssistItemKind.Macro),
        new("kbd", Strings.AssistInlineKbdLabel, "kbd:[${1:Ctrl+S}]$0",
            Strings.AssistInlineKbdDesc, AssistItemKind.Macro),
        new("btn", Strings.AssistInlineBtnLabel, "btn:[${1:OK}]$0",
            Strings.AssistInlineBtnDesc, AssistItemKind.Macro),
        new("menu", Strings.AssistInlineMenuLabel, "menu:${1:File}[${2:Save}]$0",
            Strings.AssistInlineMenuDesc, AssistItemKind.Macro),
        new("bold", Strings.AssistInlineBoldLabel, "*${1:bold}*$0",
            Strings.AssistInlineBoldDesc, AssistItemKind.Inline),
        new("italic", Strings.AssistInlineItalicLabel, "_${1:italic}_$0",
            Strings.AssistInlineItalicDesc, AssistItemKind.Inline),
        new("mono", Strings.AssistInlineMonoLabel, "`${1:code}`$0",
            Strings.AssistInlineMonoDesc, AssistItemKind.Inline),
        new("super", Strings.AssistInlineSuperLabel, "^${1:sup}^$0",
            Strings.AssistInlineSuperDesc, AssistItemKind.Inline),
        new("sub", Strings.AssistInlineSubLabel, "~${1:sub}~$0",
            Strings.AssistInlineSubDesc, AssistItemKind.Inline),
        new("ref", Strings.AssistInlineRefLabel, "<<${1:id}>>$0",
            Strings.AssistInlineRefDesc, AssistItemKind.Macro),
        new("mark", Strings.AssistInlineMarkLabel, "#${1:text}#$0",
            Strings.AssistInlineMarkDesc, AssistItemKind.Inline),
        new("pass", Strings.AssistInlinePassLabel, "pass:[${1:text}]$0",
            Strings.AssistInlinePassDesc, AssistItemKind.Macro),
        new("stem", Strings.AssistInlineStemLabel, "stem:[${1:equation}]$0",
            Strings.AssistInlineStemDesc, AssistItemKind.Macro),
        new("anchorinline", Strings.AssistInlineAnchorinlineLabel, "[[${1:id}]]$0",
            Strings.AssistInlineAnchorinlineDesc, AssistItemKind.Macro),
    };

    // --- Block attribute names (inside [ … ]) ---
    private static readonly AssistItem[] Attributes =
    {
        new("source", "source,<lang>", "source,${1:text}",
            Strings.AssistAttrSourceDesc, AssistItemKind.Attribute),
        new("cols", "cols", "cols=\"${1:1,1}\"",
            Strings.AssistAttrColsDesc, AssistItemKind.Attribute),
        new("options", "options", "options=\"${1:header}\"",
            Strings.AssistAttrOptionsDesc, AssistItemKind.Attribute),
        new("role", "role", "role=${1:name}",
            Strings.AssistAttrRoleDesc, AssistItemKind.Attribute),
        new("id", "id", "id=${1:anchor}",
            Strings.AssistAttrIdDesc, AssistItemKind.Attribute),
        new("width", "width", "width=${1:50%}",
            Strings.AssistAttrWidthDesc, AssistItemKind.Attribute),
        new("quote", "quote,<author>,<source>", "quote,${1:author},${2:source}",
            Strings.AssistAttrQuoteDesc, AssistItemKind.Attribute),
        new("align", "align", "align=${1:center}",
            Strings.AssistAttrAlignDesc, AssistItemKind.Attribute),
        new("title", "title", "title=${1:Caption}",
            Strings.AssistAttrTitleDesc, AssistItemKind.Attribute),
        new("format", "format", "format=${1:csv}",
            Strings.AssistAttrFormatDesc, AssistItemKind.Attribute),
    };

    // --- Source languages (after "source," inside the attribute list) ---
    private static readonly string[] Languages =
    {
        "text", "java", "kotlin", "csharp", "cpp", "c", "python", "javascript",
        "typescript", "ruby", "go", "rust", "bash", "shell", "json", "yaml",
        "xml", "html", "css", "sql", "groovy", "scala", "php", "swift",
    };

    // --- Built-in document attributes (after "{") ---
    private static readonly string[] DocAttributes =
    {
        "doctitle", "author", "authorinitials", "firstname", "lastname", "email",
        "revnumber", "revdate", "revremark", "doctype", "backend", "docdate",
        "doctime", "localdate", "sectnumlevels", "toc-title", "nbsp", "sp",
        "icons", "sectnums", "toclevels", "experimental", "idprefix",
        "idseparator", "source-highlighter", "stem", "version", "imagesdir",
    };

    /// <summary>
    /// Every block-level and inline syntax the assist layer knows, for the
    /// syntax-reference panel — one source of truth with completion, so the
    /// reference can never drift from what the editor actually offers.
    /// </summary>
    public static IReadOnlyList<AssistItem> ReferenceItems { get; } =
        [.. Blocks, .. Inlines];

    public static IReadOnlyList<AssistItem> PoolFor(AssistQuery query)
    {
        switch (query.Kind)
        {
            case AssistContextKind.BlockStart:
                return Blocks;

            case AssistContextKind.Inline:
                return Inlines;

            case AssistContextKind.AttributeList:
                // After "source," the only sensible completions are languages.
                if (EndsWithSourceComma(query.Lead))
                    return Languages
                        .Select(l => new AssistItem(l, l, l,
                            string.Format(Strings.AssistSourceLanguage, l), AssistItemKind.Language))
                        .ToList();
                return Attributes;

            case AssistContextKind.AttributeReference:
                // Insert the rest of the name and close the brace.
                return DocAttributes
                    .Select(a => new AssistItem(a, a, a + "}",
                        string.Format(Strings.AssistDocumentAttribute, "{" + a + "}"), AssistItemKind.Attribute))
                    .ToList();

            default:
                return Array.Empty<AssistItem>();
        }
    }

    public static IReadOnlyList<AssistItem> ItemsFor(AssistQuery query)
    {
        var pool = PoolFor(query);
        if (string.IsNullOrEmpty(query.Word))
            return pool;
        return pool
            .Where(i => i.Trigger.StartsWith(query.Word, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool EndsWithSourceComma(string lead)
    {
        var i = lead.LastIndexOf('[');
        var inner = i >= 0 ? lead[(i + 1)..] : lead;
        inner = inner.TrimEnd();
        return inner.EndsWith("source,", StringComparison.OrdinalIgnoreCase)
            || inner.Equals("source,", StringComparison.OrdinalIgnoreCase);
    }
}
