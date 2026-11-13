namespace Ascolotl.Tests.Unit.Export;

using AdocNet.Converters.Html;
using Ascolotl.Core.Models;
using Ascolotl.Export;

/// <summary>
/// Tests for <see cref="ExportThemes"/> — the unified theme mapping across
/// HTML and PDF converters.
/// </summary>
public class ExportThemesTests
{
    [Theory]
    [InlineData(ExportTheme.Asciidoctor, HtmlTheme.Asciidoctor)]
    [InlineData(ExportTheme.Clean, HtmlTheme.Clean)]
    [InlineData(ExportTheme.Github, HtmlTheme.Github)]
    public void Html_MapsEachTheme(ExportTheme theme, HtmlTheme expected)
    {
        Assert.Equal(expected, ExportThemes.Html(theme));
    }

    [Fact]
    public void Pdf_ThemedVariants_SetPalette()
    {
        Assert.NotNull(ExportThemes.Pdf(ExportTheme.Github).LinkColor);
        Assert.NotNull(ExportThemes.Pdf(ExportTheme.Clean).HeadingColor);
    }

    [Fact]
    public void Pdf_Landscape_SwapsPageDimensions()
    {
        var portrait = ExportThemes.Pdf(ExportTheme.Asciidoctor);
        var landscape = ExportThemes.Pdf(ExportTheme.Asciidoctor, landscape: true);

        Assert.True(portrait.PageHeight > portrait.PageWidth);
        Assert.Equal(portrait.PageWidth, landscape.PageHeight);
        Assert.Equal(portrait.PageHeight, landscape.PageWidth);
    }

    [Fact]
    public void Pdf_ReturnsFreshInstances()
    {
        // Never a shared preset — mutating one caller's options must not leak.
        Assert.NotSame(
            ExportThemes.Pdf(ExportTheme.Asciidoctor),
            ExportThemes.Pdf(ExportTheme.Asciidoctor));
    }

    [Fact]
    public void ExportProfile_DefaultsToAsciidoctorTheme()
    {
        Assert.Equal(ExportTheme.Asciidoctor, new ExportProfile().Theme);
    }
}
