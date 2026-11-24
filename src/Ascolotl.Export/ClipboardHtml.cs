using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Converters.Html;
using Ascolotl.Core.Services;

namespace Ascolotl.Export;

/// <summary>
/// Blocks copied from the page as HTML, for the apps that paste it formatted: Teams, Outlook,
/// Word, and GitHub, which turns a pasted table into a Markdown one.
/// </summary>
public static partial class ClipboardHtml
{
    /// <summary>The HTML of <paramref name="source"/>, a run of whole blocks. Tables carry their borders
    /// in their tags: the page's stylesheet doesn't travel with a paste.</summary>
    public static string Fragment(string source, bool markdown, string? basePath)
    {
        var parsed = DocumentParser.Parse(source, markdown, basePath);
        var html = new HtmlRenderer().RenderToString(parsed.Document, new HtmlRenderOptions
        {
            BaseDirectory = basePath,
            StemRenderer = Formulas.Renderer,
        });
        html = TableTag().Replace(html, TableAttributes);
        return CellTag().Replace(html, m => $"<{m.Groups[1].Value} {CellAttributes}");
    }

    /// <summary>A table's borders in its own tag, for a paste without the stylesheet.</summary>
    public const string TableAttributes = "<table border=\"1\" cellpadding=\"4\" style=\"border-collapse:collapse\"";

    /// <summary>A cell's borders and padding in its own tag.</summary>
    public const string CellAttributes = "style=\"border:1px solid #999;padding:3px 8px;text-align:left;vertical-align:top\"";

    /// <summary>This platform's clipboard format for HTML: Windows' name, a Mac's type identifier, X11's mime type.</summary>
    public static string PlatformFormat =>
        OperatingSystem.IsWindows() ? "HTML Format" : OperatingSystem.IsMacOS() ? "public.html" : "text/html";

    /// <summary>The fragment as <see cref="PlatformFormat"/> wants it.</summary>
    public static byte[] PlatformBytes(string fragment) =>
        OperatingSystem.IsWindows() ? WindowsFormat(fragment) : Encoding.UTF8.GetBytes(fragment);

    /// <summary>Windows' "HTML Format": a header with the byte offsets of the document and of the fragment, then both.</summary>
    public static byte[] WindowsFormat(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string before = "<html><body>\r\n<!--StartFragment-->";
        const string after = "<!--EndFragment-->\r\n</body></html>";
        var start = string.Format(header, 0, 0, 0, 0).Length; // ASCII: as many bytes as characters
        var fragmentStart = start + Encoding.UTF8.GetByteCount(before);
        var fragmentEnd = fragmentStart + Encoding.UTF8.GetByteCount(fragment);
        var end = fragmentEnd + Encoding.UTF8.GetByteCount(after);
        return Encoding.UTF8.GetBytes(string.Format(header, start, end, fragmentStart, fragmentEnd) + before + fragment + after);
    }

    [GeneratedRegex(@"<table(?=[\s>])")]
    private static partial Regex TableTag();

    [GeneratedRegex(@"<(th|td)(?=[\s>])")]
    private static partial Regex CellTag();
}
