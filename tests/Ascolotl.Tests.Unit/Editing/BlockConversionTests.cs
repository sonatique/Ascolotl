namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;

public class BlockConversionTests
{
    [Theory]
    [InlineData("Some words.", BlockKind.Heading2, false, "== Some words.")]
    [InlineData("Some words.", BlockKind.Heading2, true, "## Some words.")]
    [InlineData("=== Title", BlockKind.Paragraph, false, "Title")]
    [InlineData("# Title", BlockKind.Heading3, true, "### Title")]
    [InlineData("One\ntwo", BlockKind.BulletedList, false, "* One\n* two")]
    [InlineData("One\ntwo", BlockKind.NumberedList, true, "1. One\n2. two")]
    [InlineData("* One\n* two", BlockKind.NumberedList, false, ". One\n. two")]
    [InlineData("- One\n- two", BlockKind.Paragraph, true, "One\ntwo")]
    [InlineData("Words.", BlockKind.Quote, false, "____\nWords.\n____")]
    [InlineData("Words.", BlockKind.Quote, true, "> Words.")]
    [InlineData("> Words.", BlockKind.Paragraph, true, "Words.")]
    [InlineData("____\nWords.\n____", BlockKind.Paragraph, false, "Words.")]
    [InlineData("Words.", BlockKind.CodeBlock, false, "[source]\n----\nWords.\n----")]
    [InlineData("Words.", BlockKind.CodeBlock, true, "```\nWords.\n```")]
    [InlineData("[source,python]\n----\n- x = 1\n----", BlockKind.Paragraph, false, "- x = 1")] // code keeps its -
    [InlineData("[source,python,start=3]\n----\nx\n----", BlockKind.Paragraph, false, "x")]
    [InlineData("```js\n* a\n```", BlockKind.Quote, true, "> * a")]
    [InlineData("Words.", BlockKind.Note, false, "NOTE: Words.")]
    [InlineData("NOTE: Words.", BlockKind.Heading1, false, "= Words.")]
    [InlineData("One\ntwo", BlockKind.Heading2, false, "== One two")] // a heading holds one line
    [InlineData("One\r\ntwo", BlockKind.BulletedList, false, "* One\r\n* two")]
    public void TurnInto_KeepsTheWords_SwapsTheMarkers(string block, BlockKind kind, bool markdown, string expected) =>
        Assert.Equal(expected, BlockConversion.TurnInto(block, kind, markdown));
}
