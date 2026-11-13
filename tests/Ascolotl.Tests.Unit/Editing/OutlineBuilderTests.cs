namespace Ascolotl.Tests.Unit.Editing;

using AdocNet;
using AdocNet.Parser;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;

/// <summary>
/// Tests for <see cref="OutlineBuilder"/>, which derives the outline from the
/// parsed AST (document title + section tree) and maps section positions back
/// to 0-based editor lines.
/// </summary>
public class OutlineBuilderTests
{
    private static List<OutlineItem> BuildFrom(string source)
    {
        var result = AdocParser.Parse(source, ParseOptions.Default);
        return OutlineBuilder.Build(result.Document, result.LineOrigins);
    }

    [Fact]
    public void Build_NullDocument_IsEmpty()
    {
        Assert.Empty(OutlineBuilder.Build(null, null));
    }

    [Fact]
    public void Build_TitleAndNestedSections()
    {
        // 0:= Doc Title 1: 2:Intro 3: 4:== First 5: 6:text 7: 8:=== Sub A
        // 9: 10:== Second
        var src = "= Doc Title\n\nIntro.\n\n== First Section\n\ntext\n\n=== Sub A\n\n== Second Section\n";
        var items = BuildFrom(src);

        Assert.Equal(
            new[]
            {
                new OutlineItem("Doc Title", 1, 0),
                new OutlineItem("First Section", 2, 4),
                new OutlineItem("Sub A", 3, 8),
                new OutlineItem("Second Section", 2, 10),
            },
            items);
    }

    [Fact]
    public void Build_NoDocTitle_OnlySections()
    {
        var src = "== Only Section\n\nbody\n";
        var items = BuildFrom(src);

        var item = Assert.Single(items);
        Assert.Equal(new OutlineItem("Only Section", 2, 0), item);
    }

    [Fact]
    public void Build_LevelsFollowEqualsCount()
    {
        var src = "= T\n\n== L2\n\n=== L3\n\n==== L4\n";
        var items = BuildFrom(src);

        Assert.Equal(new[] { 1, 2, 3, 4 }, items.Select(i => i.Level));
    }
}
