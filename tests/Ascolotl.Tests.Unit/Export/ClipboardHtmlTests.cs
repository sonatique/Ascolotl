using System.Text;
using System.Text.RegularExpressions;
using Ascolotl.Export;

namespace Ascolotl.Tests.Unit.Export;

/// <summary>Blocks copied from the page paste formatted elsewhere.</summary>
public sealed class ClipboardHtmlTests
{
    [Theory]
    [InlineData("| Offset | Sent |\n|---|---|\n| -120 | 10000 |\n", true)]
    [InlineData("|===\n| Offset | Sent\n\n| -120 | 10000\n|===\n", false)]
    public void ATable_KeepsItsBordersWithoutTheStylesheet(string source, bool markdown)
    {
        var html = ClipboardHtml.Fragment(source, markdown, null);
        Assert.Contains("<table border=\"1\"", html);
        Assert.Matches("<t[dh] style=\"border:1px solid", html);
        Assert.Contains("-120", html);
        Assert.Contains("10000", html);
        Assert.DoesNotContain("<html", html); // a fragment: the clipboard format wraps it
    }

    [Fact]
    public void WindowsFormat_PointsAtTheFragment_InBytes()
    {
        const string fragment = "<p>Größe: 5 µm</p>"; // more bytes than characters
        var bytes = ClipboardHtml.WindowsFormat(fragment);
        var text = Encoding.UTF8.GetString(bytes);
        int Offset(string name) => int.Parse(Regex.Match(text, name + @":(\d+)").Groups[1].Value);

        Assert.StartsWith("Version:0.9\r\n", text);
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, Offset("StartFragment"), Offset("EndFragment") - Offset("StartFragment")));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Offset("StartHTML"), Offset("EndHTML") - Offset("StartHTML")));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
    }
}
