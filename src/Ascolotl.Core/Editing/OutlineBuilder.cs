using System.Collections;
using AdocNet;
using AdocNet.Ast;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Core.Editing;

/// <summary>
/// Builds the document outline from AdocNet's parsed AST — the document title
/// plus the section tree — rather than re-scanning the source text. Section
/// source positions are in post-include-expansion coordinates, so they are
/// translated back to editor lines via the render's <see cref="LineOrigin"/>s;
/// a section pulled in from an include (no editor representation) is skipped,
/// matching the previous behaviour where the raw-source scan never saw inside
/// includes.
/// </summary>
public static class OutlineBuilder
{
    /// <summary>
    /// Project <paramref name="document"/> into outline items. <see cref="OutlineItem.Level"/>
    /// follows the "=-count" scheme (title = 1, <c>==</c> = 2, …); <see cref="OutlineItem.LineNumber"/>
    /// is the 0-based editor line. Returns an empty list for a null document.
    /// </summary>
    public static List<OutlineItem> Build(DocumentNode? document, IReadOnlyList<LineOrigin>? lineOrigins)
    {
        var items = new List<OutlineItem>();
        if (document is null) return items;

        // The document title (= …) carries no Source on the AST; it's
        // conventionally the top of the buffer. Level 1 matches the scheme used
        // for sections below (SectionNode.Level + 1).
        if (!string.IsNullOrEmpty(document.Title))
            items.Add(new OutlineItem(document.Title, 1, 0));

        var map = IncludeExpansionMap.FromLineOrigins(lineOrigins);
        Walk(document.Children, map, items);
        return items;
    }

    private static void Walk(IEnumerable children, int[] map, List<OutlineItem> items)
    {
        foreach (var node in children)
        {
            if (node is not SectionNode section)
                continue;

            var editorLine = IncludeExpansionMap.AstLineToEditor(
                map, section.Source.Start.Line - 1, fallback: -1);
            if (editorLine >= 0) // skip include-origin sections (no editor line)
                items.Add(new OutlineItem(section.Title, section.Level + 1, editorLine));

            Walk(section.Children, map, items);
        }
    }
}
