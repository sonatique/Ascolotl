using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using Ascolotl.Core.Models;

namespace Ascolotl.Export;

/// <summary>
/// Maps an <see cref="ExportTheme"/> onto BOTH converters: the HTML stylesheet
/// and a matching PDF palette — one theme choice, consistent output across
/// formats. PDF colors approximate each HTML theme's palette.
/// </summary>
public static class ExportThemes
{
    public static HtmlTheme Html(ExportTheme theme) => theme switch
    {
        ExportTheme.Clean => HtmlTheme.Clean,
        ExportTheme.Github => HtmlTheme.Github,
        _ => HtmlTheme.Asciidoctor,
    };

    /// <summary>A fresh, caller-owned PDF option set for the theme (defaults are
    /// A4 portrait; <paramref name="landscape"/> swaps the page dimensions).
    /// Page size is set at construction — the options' properties are init-only.</summary>
    public static PdfRenderOptions Pdf(ExportTheme theme, bool landscape = false)
    {
        var defaults = new PdfRenderOptions();
        var (w, h) = landscape
            ? (defaults.PageHeight, defaults.PageWidth)
            : (defaults.PageWidth, defaults.PageHeight);

        return theme switch
        {
            ExportTheme.Clean => new PdfRenderOptions
            {
                PageWidth = w,
                PageHeight = h,
                HeadingColor = Hex(0x333333),
                BodyColor = Hex(0x222222),
                LinkColor = Hex(0x2A6496),
                CodeBackground = Hex(0xF5F5F5),
                CodespanBackground = Hex(0xF5F5F5),
                TableHeaderBackground = Hex(0xF2F2F2),
            },
            ExportTheme.Github => new PdfRenderOptions
            {
                PageWidth = w,
                PageHeight = h,
                HeadingColor = Hex(0x1F2328),
                BodyColor = Hex(0x1F2328),
                LinkColor = Hex(0x0969DA),
                CodeBackground = Hex(0xF6F8FA),
                CodespanBackground = Hex(0xF6F8FA),
                TableHeaderBackground = Hex(0xF6F8FA),
                CodeBorderColor = Hex(0xD1D9E0),
            },
            // Asciidoctor: AdocNet's defaults already mimic asciidoctor-pdf.
            _ => new PdfRenderOptions { PageWidth = w, PageHeight = h },
        };
    }

    private static PdfColor Hex(int rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f);
}
