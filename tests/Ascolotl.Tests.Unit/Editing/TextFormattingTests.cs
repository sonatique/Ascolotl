namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing;
using static Ascolotl.Core.Editing.FormatCommand;

/// <summary>
/// Cases are written with the selection between « and », or the caret at ‸;
/// the result marks the first stop the same way (what the editor selects).
/// </summary>
public sealed class TextFormattingTests
{
    private static string Run(FormatCommand command, bool markdown, string marked)
    {
        var start = marked.IndexOf('«');
        var caret = marked.IndexOf('‸');
        var text = marked.Replace("«", "").Replace("»", "").Replace("‸", "");
        var (s, length) = start >= 0 ? (start, marked.IndexOf('»') - start - 1) : (caret, 0);

        var edit = TextFormatting.Apply(command, markdown, text, s, length);
        var result = text[..edit.Start] + edit.Replacement.Text + text[(edit.Start + edit.Length)..];
        if (edit.Replacement.Stops.Count == 0) return result;
        var stop = edit.Replacement.Stops[0];
        var at = edit.Start + stop.Start;
        return stop.Length == 0 ? result.Insert(at, "‸") : result.Insert(at + stop.Length, "»").Insert(at, "«");
    }

    [Theory]
    [InlineData(Bold, false, "a «word» b", "a *«word»* b")]
    [InlineData(Bold, false, "un«believ»able", "un**«believ»**able")]
    [InlineData(Bold, true, "a «word» b", "a **«word»** b")]
    [InlineData(Bold, false, "a« word »b", "a *«word»* b")]
    [InlineData(Bold, false, "a ‸ b", "a *‸* b")]
    [InlineData(Italic, false, "«x»", "_«x»_")]
    [InlineData(Italic, true, "«x»", "*«x»*")]
    [InlineData(Italic, true, "**«x»**", "***«x»***")]
    [InlineData(Code, true, "«x»", "`«x»`")]
    [InlineData(Code, false, "a«b»c", "a``«b»``c")]
    public void Inline_WrapsTheSelection(FormatCommand command, bool markdown, string before, string after)
        => Assert.Equal(after, Run(command, markdown, before));

    [Theory]
    [InlineData(Bold, false, "a *«word»* b", "a «word» b")]
    [InlineData(Bold, true, "a «**word**» b", "a «word» b")]
    [InlineData(Bold, true, "a **‸** b", "a ‸ b")]
    [InlineData(Bold, false, "un**«believ»**able", "un«believ»able")]
    [InlineData(Code, true, "`«x»`", "«x»")]
    public void Inline_UnwrapsWhatIsAlreadyWrapped(FormatCommand command, bool markdown, string before, string after)
        => Assert.Equal(after, Run(command, markdown, before));

    [Theory]
    [InlineData(true, "see «docs» now", "see [docs](«url») now")]
    [InlineData(false, "see «docs» now", "see link:«url»[docs] now")]
    [InlineData(true, "«https://x.org»", "[«text»](https://x.org)")]
    [InlineData(false, "«https://x.org»", "https://x.org[«text»]")]
    [InlineData(true, "‸", "[«text»](url)")]
    [InlineData(false, "‸", "link:«url»[text]")]
    public void Link(bool markdown, string before, string after)
        => Assert.Equal(after, Run(FormatCommand.Link, markdown, before));

    [Theory]
    [InlineData(true, "‸", "![«alt text»](image.png)")]
    [InlineData(false, "‸", "image::«image.png»[alt text]")]
    [InlineData(false, "see ‸ here", "see image:«image.png»[alt text] here")]
    [InlineData(true, "«logo»", "![logo](«image.png»)")]
    public void Image(bool markdown, string before, string after)
        => Assert.Equal(after, Run(FormatCommand.Image, markdown, before));

    [Theory]
    [InlineData(Heading1, true, "Title‸", "# Title‸")]
    [InlineData(Heading2, false, "Title‸", "== Title‸")]
    [InlineData(Heading2, false, "== Ti‸tle", "Title‸")]
    [InlineData(Heading3, true, "## Title‸", "### Title‸")]
    [InlineData(Heading2, true, "‸", "## ‸")]
    public void Headings(FormatCommand command, bool markdown, string before, string after)
        => Assert.Equal(after, Run(command, markdown, before));

    [Theory]
    [InlineData(BulletedList, false, "«one\ntwo»", "«* one\n* two»")]
    [InlineData(NumberedList, true, "«one\n\ntwo»", "«1. one\n\n2. two»")]
    [InlineData(NumberedList, true, "«- a\n- b»", "«1. a\n2. b»")]
    [InlineData(BulletedList, false, "«* a\n* b»", "«a\nb»")]
    [InlineData(NumberedList, true, "«  - a»", "«  1. a»")]
    [InlineData(BulletedList, false, "«one\r\ntwo»", "«* one\r\n* two»")]
    [InlineData(BulletedList, false, "«one\n»two", "«* one»\ntwo")]
    [InlineData(BulletedList, true, "‸", "- ‸")]
    public void Lists(FormatCommand command, bool markdown, string before, string after)
        => Assert.Equal(after, Run(command, markdown, before));

    [Theory]
    [InlineData(true, "«a\n\nb»", "«> a\n>\n> b»")]
    [InlineData(true, "«> a\n> b»", "«a\nb»")]
    [InlineData(false, "x\n\n«a»\n\ny", "x\n\n____\n«a»\n____\n\ny")]
    [InlineData(false, "x\n«a»\ny", "x\n\n____\n«a»\n____\n\ny")]
    public void Quote(bool markdown, string before, string after)
        => Assert.Equal(after, Run(FormatCommand.Quote, markdown, before));

    [Theory]
    [InlineData(CodeBlock, true, "‸", "```\n‸\n```")]
    [InlineData(CodeBlock, false, "one‸\ntwo\n\nnext", "one\ntwo\n\n[source]\n----\n‸\n----\n\nnext")]
    [InlineData(CodeBlock, false, "one‸\r\ntwo", "one\r\ntwo\r\n\r\n[source]\r\n----\r\n‸\r\n----")]
    [InlineData(Table, true, "a\n‸\nb", "a\n\n| «Column» | Column |\n| --- | --- |\n| Cell | Cell |\n\nb")]
    [InlineData(HorizontalRule, false, "para‸", "para\n\n'''‸")]
    [InlineData(HorizontalRule, true, "‸", "---‸")]
    public void Blocks_StandApartFromTheirNeighbours(FormatCommand command, bool markdown, string before, string after)
        => Assert.Equal(after, Run(command, markdown, before));

    [Theory]
    [InlineData("Careful‸", "NOTE: Careful‸")]
    [InlineData("NOTE: Careful‸", "Careful‸")]
    [InlineData("«a\nb»", "[NOTE]\n====\n«a\nb»\n====")]
    public void Note(string before, string after)
        => Assert.Equal(after, Run(FormatCommand.Note, false, before));

    [Fact]
    public void Note_IsAsciiDocOnly()
    {
        Assert.False(TextFormatting.Supports(FormatCommand.Note, markdown: true));
        Assert.True(TextFormatting.Supports(FormatCommand.Note, markdown: false));
        Assert.All(Enum.GetValues<FormatCommand>().Where(c => c != FormatCommand.Note),
            c => Assert.True(TextFormatting.Supports(c, markdown: true)));
    }

    [Fact]
    public void LinkWithoutSelection_TabsFromTheTextToTheUrl()
    {
        var stops = TextFormatting.Apply(FormatCommand.Link, true, "", 0, 0).Replacement.Stops;
        Assert.Equal([1, 2, 0], stops.Select(s => s.Index));
    }

    [Theory]
    [InlineData("Words", 5, false, "===== Words")]
    [InlineData("Words", 6, true, "###### Words")]
    [InlineData("== Words", 0, false, "Words")]  // 0: a plain line
    [InlineData("== Words", 2, false, "Words")]  // its own level again: plain too
    [InlineData("## Words", 4, true, "#### Words")]
    public void HeadingLevel(string line, int level, bool markdown, string expected)
    {
        var edit = TextFormatting.Heading(markdown, line, 0, 0, level);
        Assert.Equal(expected, line.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement.Text));
    }
}
