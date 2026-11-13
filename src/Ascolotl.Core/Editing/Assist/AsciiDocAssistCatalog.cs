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
        new("note", "NOTE admonition", "NOTE: ${1:Important note.}$0",
            "Single-line note admonition.", AssistItemKind.Admonition),
        new("tip", "TIP admonition", "TIP: ${1:Helpful tip.}$0",
            "Single-line tip admonition.", AssistItemKind.Admonition),
        new("important", "IMPORTANT admonition", "IMPORTANT: ${1:Take note.}$0",
            "Single-line important admonition.", AssistItemKind.Admonition),
        new("warning", "WARNING admonition", "WARNING: ${1:Be careful.}$0",
            "Single-line warning admonition.", AssistItemKind.Admonition),
        new("caution", "CAUTION admonition", "CAUTION: ${1:Proceed carefully.}$0",
            "Single-line caution admonition.", AssistItemKind.Admonition),
        new("source", "Source code block", "[source,${1:text}]\n----\n${2:code}\n----\n$0",
            "Syntax-highlighted source block.", AssistItemKind.Block),
        new("listing", "Listing block", "----\n${1:text}\n----\n$0",
            "Verbatim listing block.", AssistItemKind.Block),
        new("example", "Example block", "====\n${1:content}\n====\n$0",
            "Example (delimited) block.", AssistItemKind.Block),
        new("sidebar", "Sidebar block", "****\n${1:content}\n****\n$0",
            "Sidebar block.", AssistItemKind.Block),
        new("quote", "Quote block", "[quote,${1:author},${2:source}]\n____\n${3:Quotation.}\n____\n$0",
            "Block quotation with attribution.", AssistItemKind.Block),
        new("table", "Table", "[cols=\"${1:1,1}\"]\n|===\n| ${2:Cell} | ${3:Cell}\n\n| ${4:Cell} | ${5:Cell}\n|===\n$0",
            "Two-column table.", AssistItemKind.Block),
        new("image", "Block image", "image::${1:path}[${2:alt}]$0",
            "Block image macro.", AssistItemKind.Macro),
        new("ulist", "Bullet list", "* ${1:First item}\n* ${2:Second item}$0",
            "Unordered (bullet) list.", AssistItemKind.Block),
        new("olist", "Numbered list", ". ${1:First item}\n. ${2:Second item}$0",
            "Ordered (numbered) list.", AssistItemKind.Block),
        new("h2", "Heading level 2", "== ${1:Heading}$0",
            "Section heading, level 2.", AssistItemKind.Block),
        new("h3", "Heading level 3", "=== ${1:Heading}$0",
            "Section heading, level 3.", AssistItemKind.Block),
        new("h4", "Heading level 4", "==== ${1:Heading}$0",
            "Section heading, level 4.", AssistItemKind.Block),
        new("comment", "Line comment", "// ${1:comment}$0",
            "A line comment (not rendered).", AssistItemKind.Block),
        new("attribute", "Document attribute", ":${1:name}: ${2:value}$0",
            "Document attribute entry.", AssistItemKind.Attribute),
        new("toc", "Table of contents", ":toc:$0",
            "Enable the table of contents.", AssistItemKind.Attribute),
        new("h5", "Heading level 5", "===== ${1:Heading}$0",
            "Section heading, level 5.", AssistItemKind.Block),
        new("admonitionblock", "Admonition block", "[${1:NOTE}]\n====\n${2:content}\n====\n$0",
            "Multi-line admonition (NOTE/TIP/IMPORTANT/WARNING/CAUTION).", AssistItemKind.Admonition),
        new("include", "Include directive", "include::${1:file.adoc}[]$0",
            "Include another file's contents.", AssistItemKind.Macro),
        new("ifdef", "ifdef directive", "ifdef::${1:attribute}[]\n${2:content}\nendif::[]\n$0",
            "Render content only when an attribute is set.", AssistItemKind.Macro),
        new("ifndef", "ifndef directive", "ifndef::${1:attribute}[]\n${2:content}\nendif::[]\n$0",
            "Render content only when an attribute is NOT set.", AssistItemKind.Macro),
        new("ifeval", "ifeval directive", "ifeval::[${1:condition}]\n${2:content}\nendif::[]\n$0",
            "Render content only when a condition holds.", AssistItemKind.Macro),
        new("anchor", "Anchor", "[[${1:id}]]$0",
            "Define an anchor / cross-reference target.", AssistItemKind.Block),
        new("title", "Block title", ".${1:Title}$0",
            "Title for the following block.", AssistItemKind.Block),
        new("dlist", "Description list", "${1:Term}:: ${2:Definition}$0",
            "Description (definition) list.", AssistItemKind.Block),
        new("checklist", "Checklist", "* [ ] ${1:First task}\n* [ ] ${2:Second task}$0",
            "A checklist (task list).", AssistItemKind.Block),
        new("literal", "Literal block", "....\n${1:text}\n....\n$0",
            "Literal (monospaced, verbatim) block.", AssistItemKind.Block),
        new("verse", "Verse block", "[verse,${1:author}]\n____\n${2:lines}\n____\n$0",
            "Verse block, preserving line breaks.", AssistItemKind.Block),
        new("open", "Open block", "--\n${1:content}\n--\n$0",
            "Open (anonymous) block.", AssistItemKind.Block),
        new("passblock", "Passthrough block", "++++\n${1:raw}\n++++\n$0",
            "Passthrough block (raw output, no processing).", AssistItemKind.Block),
        new("stemblock", "STEM (math) block", "[stem]\n++++\n${1:equation}\n++++\n$0",
            "Block STEM/LaTeX equation.", AssistItemKind.Block),
        new("pagebreak", "Page break", "<<<$0",
            "Force a page break (PDF/DocBook).", AssistItemKind.Block),
        new("rule", "Thematic break", "'''$0",
            "Thematic break (horizontal rule).", AssistItemKind.Block),
        new("video", "Video macro", "video::${1:url}[]$0",
            "Embed a video.", AssistItemKind.Macro),
        new("audio", "Audio macro", "audio::${1:url}[]$0",
            "Embed an audio clip.", AssistItemKind.Macro),
    };

    // --- Inline macros and markup (mid-line) ---
    private static readonly AssistItem[] Inlines =
    {
        new("xref", "Cross reference", "xref:${1:id}[${2:text}]$0",
            "Cross-reference to an anchor or id.", AssistItemKind.Macro),
        new("link", "External link", "link:${1:https://}[${2:text}]$0",
            "External hyperlink.", AssistItemKind.Macro),
        new("image", "Inline image", "image:${1:path}[${2:alt}]$0",
            "Inline image macro.", AssistItemKind.Macro),
        new("footnote", "Footnote", "footnote:[${1:text}]$0",
            "A footnote.", AssistItemKind.Macro),
        new("kbd", "Keyboard shortcut", "kbd:[${1:Ctrl+S}]$0",
            "Render a keyboard shortcut.", AssistItemKind.Macro),
        new("btn", "UI button", "btn:[${1:OK}]$0",
            "Render a UI button.", AssistItemKind.Macro),
        new("menu", "Menu selection", "menu:${1:File}[${2:Save}]$0",
            "Render a UI menu path.", AssistItemKind.Macro),
        new("bold", "Bold text", "*${1:bold}*$0",
            "Bold (strong) text.", AssistItemKind.Inline),
        new("italic", "Italic text", "_${1:italic}_$0",
            "Italic (emphasis) text.", AssistItemKind.Inline),
        new("mono", "Monospace text", "`${1:code}`$0",
            "Monospace text.", AssistItemKind.Inline),
        new("super", "Superscript", "^${1:sup}^$0",
            "Superscript text.", AssistItemKind.Inline),
        new("sub", "Subscript", "~${1:sub}~$0",
            "Subscript text.", AssistItemKind.Inline),
        new("ref", "Cross reference <<>>", "<<${1:id}>>$0",
            "Cross-reference to an anchor (angle-bracket form).", AssistItemKind.Macro),
        new("mark", "Highlight", "#${1:text}#$0",
            "Highlighted (marked) text.", AssistItemKind.Inline),
        new("pass", "Inline passthrough", "pass:[${1:text}]$0",
            "Inline passthrough (raw, unprocessed).", AssistItemKind.Macro),
        new("stem", "Inline STEM (math)", "stem:[${1:equation}]$0",
            "Inline STEM/LaTeX equation.", AssistItemKind.Macro),
        new("anchorinline", "Inline anchor", "[[${1:id}]]$0",
            "Inline anchor / cross-reference target.", AssistItemKind.Macro),
    };

    // --- Block attribute names (inside [ … ]) ---
    private static readonly AssistItem[] Attributes =
    {
        new("source", "source,<lang>", "source,${1:text}",
            "Mark the block as source with a language.", AssistItemKind.Attribute),
        new("cols", "cols", "cols=\"${1:1,1}\"",
            "Table column specification.", AssistItemKind.Attribute),
        new("options", "options", "options=\"${1:header}\"",
            "Block options, e.g. header,footer.", AssistItemKind.Attribute),
        new("role", "role", "role=${1:name}",
            "Assign a CSS role.", AssistItemKind.Attribute),
        new("id", "id", "id=${1:anchor}",
            "Assign an id / anchor.", AssistItemKind.Attribute),
        new("width", "width", "width=${1:50%}",
            "Set the block width.", AssistItemKind.Attribute),
        new("quote", "quote,<author>,<source>", "quote,${1:author},${2:source}",
            "Mark the block as a quotation.", AssistItemKind.Attribute),
        new("align", "align", "align=${1:center}",
            "Horizontal alignment.", AssistItemKind.Attribute),
        new("title", "title", "title=${1:Caption}",
            "Caption / title for the block.", AssistItemKind.Attribute),
        new("format", "format", "format=${1:csv}",
            "Table data format (csv, tsv, dsv).", AssistItemKind.Attribute),
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
                            $"Source language: {l}.", AssistItemKind.Language))
                        .ToList();
                return Attributes;

            case AssistContextKind.AttributeReference:
                // Insert the rest of the name and close the brace.
                return DocAttributes
                    .Select(a => new AssistItem(a, a, a + "}",
                        $"Document attribute: {{{a}}}.", AssistItemKind.Attribute))
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
