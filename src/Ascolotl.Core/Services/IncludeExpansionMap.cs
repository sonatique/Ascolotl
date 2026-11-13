using AdocNet;

namespace Ascolotl.Core.Services;

/// <summary>
/// Translation table from AdocNet AST line numbers (post-include expansion)
/// back to editor-source line numbers (what the user sees). AdocNet's parser
/// expands <c>include::</c> directives before assigning source positions, so
/// AST line numbers count expanded content while the editor only shows the
/// original source. Without a translation, every line number past the first
/// include is off, and scroll-sync / click-to-source clamp wrong values.
///
/// <para>The map is a thin projection of AdocNet's authoritative per-line
/// provenance (<see cref="ParseResult.LineOrigins"/>, AdocNet 1.0.13 / #46):
/// for each post-include line, a non-synthetic origin maps to its editor line
/// (<c>SourceLine - 1</c>); a synthetic one (content pulled in from an include)
/// maps to <c>-1</c> (no direct representation in the editor). Because the
/// parser produces this, <c>tags=</c>/<c>lines=</c>/<c>leveloffset=</c> and
/// attribute-substituted includes are all handled correctly — unlike the
/// earlier hand-rolled reconstruction this replaced.</para>
///
/// Output: an array <c>map</c> where <c>map[astLine] = editorLine</c> (0-based
/// on both axes) or <c>-1</c> for AST lines with no editor representation.
/// </summary>
public static class IncludeExpansionMap
{
    /// <summary>
    /// Project AdocNet's per-line origins into the AST→editor line map. A null
    /// or empty list yields an empty map (treated as "no mapping available",
    /// so lookups fall back).
    /// </summary>
    public static int[] FromLineOrigins(IReadOnlyList<LineOrigin>? origins)
    {
        if (origins is null || origins.Count == 0) return Array.Empty<int>();

        var map = new int[origins.Count];
        for (var i = 0; i < origins.Count; i++)
        {
            var o = origins[i];
            // Synthetic lines (include-expanded content) have no editor line.
            map[i] = o.IsSynthetic ? -1 : o.SourceLine - 1;
        }
        return map;
    }

    /// <summary>
    /// Look up the editor line for a given (0-based) AST line. Returns
    /// <paramref name="fallback"/> when the AST line is out of range or
    /// came from inside an include.
    /// </summary>
    public static int AstLineToEditor(int[] map, int astLineZeroBased, int fallback = -1)
    {
        if (astLineZeroBased < 0 || astLineZeroBased >= map.Length) return fallback;
        var v = map[astLineZeroBased];
        return v < 0 ? fallback : v;
    }
}
