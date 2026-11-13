using System.Globalization;
using System.Text;
using AdocNet.Converters.Pdf;

namespace Ascolotl.Export;

/// <summary>
/// Translates a loaded style (asciidoctor-pdf theme, as <see cref="PdfRenderOptions"/>)
/// into CSS appended after the HTML/EPUB base stylesheet, so HTML and EPUB
/// exports carry the same colours and sizes as the PDF.
/// </summary>
public static class StyleCss
{
    // ponytail: fonts are named, not embedded: they render where the style's
    // fonts are installed (the theme's case). Embed via @font-face if shared HTML
    // must keep them elsewhere.
    public static string From(PdfRenderOptions o)
    {
        var css = new StringBuilder();
        Rule(css, "body", ("color", o.BodyColor), ("font-size", Pt(o.FontSize)), ("font-family", Family(o.FontPath, "sans-serif")));
        Rule(css, "h1, h2, h3, h4, h5, h6, #toctitle", ("font-family", Family(o.HeadingFontPath, "sans-serif")));
        Rule(css, "code, pre, kbd", ("font-family", Family(o.MonoFontPath, "monospace")));
        Rule(css, "#header>h1:first-child, h1", ("color", o.HeadingColor), ("font-size", Pt(o.TitleFontSize)));
        Rule(css, "h2", ("color", o.Heading2Color ?? o.HeadingColor), ("font-size", Pt(o.Heading2FontSize)));
        Rule(css, "h3", ("color", o.Heading3Color ?? o.HeadingColor), ("font-size", Pt(o.Heading3FontSize)));
        Rule(css, "h4", ("color", o.Heading4Color ?? o.HeadingColor), ("font-size", Pt(o.Heading4FontSize)));
        Rule(css, "h5", ("color", o.Heading5Color ?? o.HeadingColor), ("font-size", Pt(o.Heading5FontSize)));
        Rule(css, "a", ("color", o.LinkColor));
        Rule(css, ".listingblock pre, .literalblock pre",
            ("background", o.CodeBackground), ("border-color", o.CodeBorderColor), ("font-size", Pt(o.CodeFontSize)));
        Rule(css, ":not(pre)>code", ("background", o.CodespanBackground), ("color", o.CodespanColor));
        Rule(css, "table.tableblock, th.tableblock, td.tableblock", ("border-color", o.TableBorderColor));
        Rule(css, "th.tableblock, th.tableblock *", ("background", o.TableHeaderBackground), ("color", o.TableHeaderFontColor));
        return css.ToString();
    }

    private static string? Family(string? fontPath, string generic)
        => TrueTypeFontInfo.ReadFamilyName(fontPath ?? "") is { } name ? $"\"{name}\", {generic}" : null;

    private static string? Pt(float? size)
        => size is { } s ? s.ToString("0.##", CultureInfo.InvariantCulture) + "pt" : null;

    // Values win over the base stylesheet's more specific selectors via !important.
    private static void Rule(StringBuilder css, string selector, params (string Name, object? Value)[] declarations)
    {
        var body = string.Concat(declarations
            .Select(d => (d.Name, Value: d.Value is PdfColor c ? Hex(c) : d.Value as string))
            .Where(d => d.Value is not null)
            .Select(d => $"{d.Name}:{d.Value} !important;"));
        if (body.Length > 0)
            css.Append(selector).Append('{').Append(body).Append("}\n");
    }

    private static string Hex(PdfColor c)
        => $"#{Byte(c.R):x2}{Byte(c.G):x2}{Byte(c.B):x2}";

    private static int Byte(float channel) => (int)Math.Round(Math.Clamp(channel, 0f, 1f) * 255);
}
