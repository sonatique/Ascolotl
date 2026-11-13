namespace Ascolotl.Core.Editing;

/// <summary>
/// Pure logic for inserting a new AsciiDoc block into a source document at a
/// block boundary. Everything in the block-WYSIWYG model is a text splice;
/// these helpers normalize the blank-line separation AsciiDoc requires
/// between blocks so an insertion never merges into a neighbour or leaves a
/// run of blank lines.
/// </summary>
public static class BlockInsertion
{
    /// <summary>
    /// Join two source fragments with exactly one blank line between them
    /// (when both are non-empty). Only the newlines at the seam are touched,
    /// so structure elsewhere — including inside verbatim blocks — is left
    /// alone.
    /// </summary>
    public static string Join(string left, string right)
    {
        var l = left.TrimEnd('\n');
        var r = right.TrimStart('\n');
        if (l.Length == 0) return right; // left carries no block content
        if (r.Length == 0) return left;  // right carries no block content
        return l + "\n\n" + r;
    }

    /// <summary>
    /// Insert <paramref name="snippet"/> as a new block immediately after the
    /// block whose content ends at exclusive offset <paramref name="blockEnd"/>.
    /// </summary>
    public static string InsertAfter(string source, int blockEnd, string snippet)
    {
        blockEnd = Math.Clamp(blockEnd, 0, source.Length);
        return Join(source[..blockEnd], Join(snippet, source[blockEnd..]));
    }

    /// <summary>
    /// Insert <paramref name="snippet"/> as a new block immediately before the
    /// block whose content starts at offset <paramref name="blockStart"/>.
    /// </summary>
    public static string InsertBefore(string source, int blockStart, string snippet)
    {
        blockStart = Math.Clamp(blockStart, 0, source.Length);
        return Join(source[..blockStart], Join(snippet, source[blockStart..]));
    }

    /// <summary>Append <paramref name="snippet"/> as a new block at the end of
    /// the document (also the natural path for an empty document).</summary>
    public static string Append(string source, string snippet) => Join(source, snippet);
}

/// <summary>
/// AsciiDoc source templates for the block types the WYSIWYG insert menu
/// offers. Each is a self-contained block (no surrounding blank lines —
/// <see cref="BlockInsertion"/> adds the separation).
/// </summary>
public static class BlockSnippets
{
    public const string Paragraph = "New paragraph.";

    /// <summary>Section heading at the given level (1 → <c>==</c>, the first
    /// real section under the document title).</summary>
    public static string Heading(int level) =>
        new string('=', Math.Clamp(level, 1, 5) + 1) + " New heading";

    public const string BulletList = "* First item\n* Second item";
    public const string NumberedList = ". First item\n. Second item";

    public const string Table =
        "[cols=\"1,1\"]\n|===\n| Cell | Cell\n\n| Cell | Cell\n|===";

    public const string CodeBlock = "[source]\n----\ncode\n----";

    public const string Note = "NOTE: Your note here.";

    public const string ThematicBreak = "'''";
}
