namespace Ascolotl.Tests.Unit.Export;

using AdocNet.Ast;
using AdocNet.Converters.Pdf;
using Ascolotl.Export;

public sealed class TitlePageLayoutTests
{
    private static Dictionary<string, string> Attrs(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Partition_SplitsAtLastSeparator()
    {
        var (main, sub) = TitlePageLayout.Partition("Guide: Part A: The details", Attrs());

        Assert.Equal("Guide: Part A", main);
        Assert.Equal("The details", sub);
    }

    [Fact]
    public void Partition_HonoursTitleSeparator_AndNoSubtitle()
    {
        Assert.Equal(("Guide", "Sub"), TitlePageLayout.Partition("Guide | Sub", Attrs(("title-separator", " |"))));
        Assert.Equal(("Only a title", null), TitlePageLayout.Partition("Only a title", Attrs()));
    }

    [Fact]
    public void HardLineBreak_IsSpaceInHeading_LineOnTitlePage()
    {
        Assert.Equal("Packet Format User Manual", TitlePageLayout.PlainTitle("Packet Format +\nUser Manual"));
        Assert.Equal("Packet Format\nUser Manual", TitlePageLayout.TitlePageText("Packet Format +\nUser Manual"));
    }

    [Fact]
    public void RevisionAndAuthorLines()
    {
        Assert.Equal("Version 1.0, 2026-10-02: Draft",
            TitlePageLayout.RevisionLine(Attrs(("revnumber", "1.0"), ("revdate", "2026-10-02"), ("revremark", "Draft"))));
        Assert.Equal("Rev 2", TitlePageLayout.RevisionLine(Attrs(("revnumber", "2"), ("version-label", "Rev"))));
        Assert.Null(TitlePageLayout.RevisionLine(Attrs()));
        Assert.Equal("Ann, Bob", TitlePageLayout.AuthorLine(Attrs(("author", "Ann"), ("author_2", "Bob"))));
        Assert.Null(TitlePageLayout.AuthorLine(Attrs()));
    }

    [Theory]
    [InlineData("130", 64 + 130)]
    [InlineData("10%", 64 + (842 - 64 - 68) * 0.10)]
    [InlineData("10vh", 84.2)]
    [InlineData("1in", 64 + 72)]
    public void ResolveTop_FromPageTop(string top, double expected)
    {
        var o = new PdfRenderOptions { PageWidth = 595, PageHeight = 842, MarginTop = 64, MarginBottom = 68 };

        Assert.Equal(expected, TitlePageLayout.ResolveTop(top, o), 3);
    }

    [Theory]
    [InlineData("= Doc\n\nText.\n", false)]
    [InlineData("= Doc\n:title-page:\n\nText.\n", true)]
    [InlineData("= Doc\n:doctype: book\n\nText.\n", true)]
    [InlineData("= Doc\n:title-page:\n:notitle:\n\nText.\n", false)]
    [InlineData(":title-page:\n\nText.\n", false)]
    public void Wanted_FollowsAsciidoctorPdfRules(string source, bool expected)
        => Assert.Equal(expected, TitlePageLayout.Wanted(AdocNet.Parser.AdocParser.Parse(source).Document));
}
