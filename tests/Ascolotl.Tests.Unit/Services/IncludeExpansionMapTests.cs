namespace Ascolotl.Tests.Unit.Services;

using AdocNet;
using Ascolotl.Core.Services;

/// <summary>
/// Tests for <see cref="IncludeExpansionMap"/>, which projects AdocNet's
/// authoritative per-line origins (#46) into the AST→editor line map and
/// looks lines up with an include-aware fallback.
/// </summary>
public class IncludeExpansionMapTests
{
    private static LineOrigin Editor(int sourceLine) => new("main.adoc", sourceLine, false);
    private static LineOrigin Included(int sourceLine) => new("part.adoc", sourceLine, true);

    [Fact]
    public void FromLineOrigins_NullOrEmpty_IsEmpty()
    {
        Assert.Empty(IncludeExpansionMap.FromLineOrigins(null));
        Assert.Empty(IncludeExpansionMap.FromLineOrigins(Array.Empty<LineOrigin>()));
    }

    [Fact]
    public void FromLineOrigins_NoInclude_IsIdentity()
    {
        // 1-based SourceLine 1..4 -> 0-based editor lines 0..3.
        var origins = new[] { Editor(1), Editor(2), Editor(3), Editor(4) };
        Assert.Equal(new[] { 0, 1, 2, 3 }, IncludeExpansionMap.FromLineOrigins(origins));
    }

    [Fact]
    public void FromLineOrigins_SyntheticLinesMapToMinusOne()
    {
        // main 1..4, then 3 synthetic include lines, then main 6,7,8 —
        // mirrors the verified main.adoc/part.adoc expansion.
        var origins = new[]
        {
            Editor(1), Editor(2), Editor(3), Editor(4),
            Included(1), Included(2), Included(3),
            Editor(6), Editor(7), Editor(8),
        };
        Assert.Equal(
            new[] { 0, 1, 2, 3, -1, -1, -1, 5, 6, 7 },
            IncludeExpansionMap.FromLineOrigins(origins));
    }

    [Fact]
    public void AstLineToEditor_MapsAndFallsBack()
    {
        // map: ast 0->ed 0, ast 1->include (-1), ast 2->ed 1, ast 3->ed 2
        var map = new[] { 0, -1, 1, 2 };

        Assert.Equal(0, IncludeExpansionMap.AstLineToEditor(map, 0));
        Assert.Equal(1, IncludeExpansionMap.AstLineToEditor(map, 2));
        Assert.Equal(2, IncludeExpansionMap.AstLineToEditor(map, 3));

        // Include line, out-of-range (low/high), and explicit fallback value.
        Assert.Equal(-99, IncludeExpansionMap.AstLineToEditor(map, 1, fallback: -99));
        Assert.Equal(-99, IncludeExpansionMap.AstLineToEditor(map, -1, fallback: -99));
        Assert.Equal(-99, IncludeExpansionMap.AstLineToEditor(map, 100, fallback: -99));
        Assert.Equal(-1, IncludeExpansionMap.AstLineToEditor(map, 1)); // default fallback
    }
}
