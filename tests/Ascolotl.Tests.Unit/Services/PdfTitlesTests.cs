namespace Ascolotl.Tests.Unit.Services;

using Ascolotl.Core.Models;

public sealed class PdfTitlesTests
{
    [Theory]
    [InlineData("Packet Format User Manual: Importing and exporting", PdfTitleRule.FullTitle,
        "Packet Format User Manual: Importing and exporting")]
    [InlineData("Packet Format User Manual: Importing and exporting", PdfTitleRule.UpToFirstColon,
        "Packet Format User Manual")]
    [InlineData("A: B: C", PdfTitleRule.UpToFirstColon, "A")]
    [InlineData("No subtitle", PdfTitleRule.UpToFirstColon, "No subtitle")]
    [InlineData(": leading colon", PdfTitleRule.UpToFirstColon, ": leading colon")]
    [InlineData("Packet Format +\nUser Manual: Sub", PdfTitleRule.UpToFirstColon, "Packet Format User Manual")]
    [InlineData("Two\nlines", PdfTitleRule.FullTitle, "Two lines")]
    public void From_AppliesRuleToPlainTitle(string doctitle, PdfTitleRule rule, string expected)
        => Assert.Equal(expected, PdfTitles.From(doctitle, rule));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void From_Untitled_IsNull(string? doctitle)
        => Assert.Null(PdfTitles.From(doctitle, PdfTitleRule.UpToFirstColon));
}
