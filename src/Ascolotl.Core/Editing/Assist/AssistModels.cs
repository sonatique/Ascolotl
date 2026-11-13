namespace Ascolotl.Core.Editing.Assist;

/// <summary>Where the caret sits, which decides what completions make sense.</summary>
public enum AssistContextKind
{
    /// <summary>No useful completion context.</summary>
    None,

    /// <summary>Start of a line at block level (only whitespace before the word).
    /// Offers block scaffolds, admonitions, headings, lists, attributes.</summary>
    BlockStart,

    /// <summary>Inside an unclosed block attribute list <c>[ … </c> on its own
    /// line. Offers attribute names and source languages.</summary>
    AttributeList,

    /// <summary>Just after a <c>{</c> — an attribute reference. Offers known
    /// document attributes (built-in plus those defined in the document).</summary>
    AttributeReference,

    /// <summary>Just after <c>&lt;&lt;</c> — a cross-reference. Offers anchor
    /// ids defined in the document (explicit anchors and section ids).</summary>
    CrossReference,

    /// <summary>Mid-line prose. Offers inline macros and markup.</summary>
    Inline,
}

/// <summary>Loose grouping for an item (drives ordering/labelling, not behavior).</summary>
public enum AssistItemKind { Block, Admonition, Macro, Attribute, Inline, Language }

/// <summary>What to complete and the span of partial text to replace.</summary>
/// <param name="Kind">The detected context.</param>
/// <param name="Word">The partial token already typed (used to filter and to
/// determine the replacement span).</param>
/// <param name="ReplaceStart">Absolute offset where <paramref name="Word"/> begins.</param>
/// <param name="CaretOffset">Absolute caret offset.</param>
/// <param name="Lead">The current line's text from its start up to
/// <paramref name="ReplaceStart"/> — lets the catalog refine (e.g. offer source
/// languages after <c>source,</c>).</param>
public readonly record struct AssistQuery(
    AssistContextKind Kind,
    string Word,
    int ReplaceStart,
    int CaretOffset,
    string Lead);

/// <summary>A single completion suggestion.</summary>
/// <param name="Trigger">Keyword matched against what the user typed.</param>
/// <param name="Label">Display text in the completion list.</param>
/// <param name="Snippet">Insertion template; may carry tab-stops in
/// <c>${n:placeholder}</c> / <c>$0</c> form (see <see cref="Snippet"/>).</param>
/// <param name="Description">One-line help shown beside the list — the "inline
/// syntax help".</param>
/// <param name="Kind">Loose grouping.</param>
public sealed record AssistItem(
    string Trigger,
    string Label,
    string Snippet,
    string Description,
    AssistItemKind Kind = AssistItemKind.Block);
