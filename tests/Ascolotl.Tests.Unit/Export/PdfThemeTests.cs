namespace Ascolotl.Tests.Unit.Export;

using System.Text;
using System.Text.RegularExpressions;
using Ascolotl.Core.Models;
using Ascolotl.Export;

/// <summary>
/// PDF theme selection (app setting vs. document attributes), font-root
/// fallback, and includes that leave the document's folder.
/// </summary>
public sealed class PdfThemeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ascolotl-theme-{Guid.NewGuid():N}");
    private readonly string _docDir;

    public PdfThemeTests()
    {
        _docDir = Path.Combine(_root, "docs", "guide");
        Directory.CreateDirectory(_docDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Dictionary<string, string> Attrs(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void ResolveThemePath_NoAttribute_UsesAppSetting()
    {
        Assert.Equal("app.yml", PdfThemeResolver.ResolveThemePath(Attrs(), _docDir, "app.yml"));
        Assert.Null(PdfThemeResolver.ResolveThemePath(Attrs(), _docDir, null));
    }

    [Fact]
    public void ResolveThemePath_DocumentAttribute_OverridesAppSetting()
    {
        var path = PdfThemeResolver.ResolveThemePath(Attrs(("pdf-theme", "corp.yml")), _docDir, "app.yml");

        Assert.Equal(Path.Combine(_docDir, "corp.yml"), path);
    }

    [Fact]
    public void ResolveThemePath_BareName_MapsToThemeFileInThemesDir()
    {
        var path = PdfThemeResolver.ResolveThemePath(
            Attrs(("pdf-theme", "corp"), ("pdf-themesdir", "../themes")), _docDir, null);

        Assert.Equal(Path.GetFullPath(Path.Combine(_docDir, "..", "themes", "corp-theme.yml")), path);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("base")]
    public void ResolveThemePath_BuiltinName_ForcesBuiltinLook(string name)
    {
        Assert.Null(PdfThemeResolver.ResolveThemePath(Attrs(("pdf-theme", name)), _docDir, "app.yml"));
    }

    [Fact]
    public void Load_FindsFontsUnderDocumentFontsDir()
    {
        // The theme's catalog says fonts/body.ttf; the fonts live under a
        // separate root that only pdf-fontsdir names (as adoc2pdf.bat does).
        var fontRoot = Path.Combine(_root, "fontroot");
        Directory.CreateDirectory(Path.Combine(fontRoot, "fonts"));
        File.Copy(AnyTrueTypeFont(), Path.Combine(fontRoot, "fonts", "body.ttf"));
        var theme = WriteTheme("""
            extends: default
            font:
              catalog:
                Body:
                  normal: fonts/body.ttf
                  bold: fonts/body.ttf
                  italic: fonts/body.ttf
                  bold_italic: fonts/body.ttf
            base:
              font-family: Body
            """);

        var withoutRoot = PdfThemeResolver.Load(theme, Attrs(), _docDir);
        var withRoot = PdfThemeResolver.Load(theme, Attrs(("pdf-fontsdir", fontRoot)), _docDir);

        Assert.True(string.IsNullOrEmpty(withoutRoot.FontPath));
        Assert.Equal(Path.Combine(fontRoot, "fonts", "body.ttf"), withRoot.FontPath);
    }

    [Fact]
    public async Task Export_MissingDocumentTheme_FailsWithPath()
    {
        var result = await Export("= Doc\n:pdf-theme: nowhere.yml\n\nText.\n", ExportFormat.Pdf, null);

        Assert.False(result.IsSuccess);
        Assert.Contains("nowhere.yml", result.ErrorMessage);
    }

    [Fact]
    public async Task Export_LandscapeWithTheme_RotatesThemePage()
    {
        var theme = WriteTheme("extends: default\nheading-h2:\n  font-color: #365f91\n");

        // Landscape first: rotation must not leak into the next export of the
        // same theme (it would if the loader cached and shared instances).
        var landscape = await ExportBytes("= Doc\n\n== A\n\nText.\n", ExportFormat.PdfLandscape, theme);
        var portrait = await ExportBytes("= Doc\n\n== A\n\nText.\n", ExportFormat.Pdf, theme);

        var (pw, ph) = MediaBox(portrait);
        var (lw, lh) = MediaBox(landscape);
        Assert.True(pw < ph, "portrait page should be taller than wide");
        Assert.Equal((ph, pw), (lw, lh));
    }

    [Fact]
    public async Task Export_LandscapeWithoutTheme_DoesNotRotateLaterPortraitExports()
    {
        await ExportBytes("= Doc\n\nText.\n", ExportFormat.PdfLandscape, null);
        var (w, h) = MediaBox(await ExportBytes("= Doc\n\nText.\n", ExportFormat.Pdf, null));

        Assert.True(w < h);
    }

    [Fact]
    public async Task Export_PdfTitle_ReplacesMetadataTitleOnly()
    {
        const string source = "= Manual: The long subtitle\n\nText.\n";

        var plain = Encoding.Latin1.GetString(await ExportBytes(source, ExportFormat.Pdf, null));
        var titled = Encoding.Latin1.GetString(await ExportBytes(source, ExportFormat.PdfLandscape, null, "Manual"));

        Assert.Contains("/Title (Manual: The long subtitle)", plain);
        Assert.DoesNotContain("/Title (Manual)", plain);
        Assert.Contains("/Title (Manual)", titled);
        // The outline (bookmarks) keeps the full document title.
        Assert.Contains("/Title (Manual: The long subtitle)", titled);
        var (w, h) = MediaBox(Encoding.Latin1.GetBytes(titled));
        Assert.True(w > h, "landscape page should be wider than tall");
    }

    [Fact]
    public async Task Export_HtmlWithStyle_CarriesStyleColours()
    {
        var theme = WriteTheme("""
            extends: default
            heading-h2:
              font-color: #365f91
            table:
              head:
                background-color: #4f81bd
            """);

        var styled = Encoding.UTF8.GetString(await ExportBytes("= Doc\n\n== A\n\nText.\n", ExportFormat.Html, theme));
        var plain = Encoding.UTF8.GetString(await ExportBytes("= Doc\n\n== A\n\nText.\n", ExportFormat.Html, null));

        Assert.Contains("h2{color:#365f91 !important;", styled);
        Assert.Contains("background:#4f81bd !important;", styled);
        Assert.DoesNotContain("#365f91", plain);
    }

    [Fact]
    public async Task Export_HtmlWithStyle_NamesThemeFonts()
    {
        var font = AnyTrueTypeFont();
        var family = AdocNet.Converters.Pdf.TrueTypeFontInfo.ReadFamilyName(font);
        Assert.NotNull(family);
        Directory.CreateDirectory(Path.Combine(_root, "fonts"));
        File.Copy(font, Path.Combine(_root, "fonts", "body.ttf"));
        var theme = WriteTheme("""
            extends: default
            font:
              catalog:
                Body:
                  normal: fonts/body.ttf
                  bold: fonts/body.ttf
                  italic: fonts/body.ttf
                  bold_italic: fonts/body.ttf
            base:
              font-family: Body
            """);

        var html = Encoding.UTF8.GetString(await ExportBytes("= Doc\n\nText.\n", ExportFormat.Html, theme));

        Assert.Contains($"font-family:\"{family}\", sans-serif !important;", html);
    }

    [Fact]
    public async Task Export_EpubWithStyle_CarriesStyleColours()
    {
        var theme = WriteTheme("extends: default\nheading-h2:\n  font-color: #365f91\n");

        var epub = await ExportBytes("= Doc\n\n== A\n\nText.\n", ExportFormat.Epub, theme);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(epub));
        var css = string.Concat(zip.Entries.Where(e => e.FullName.EndsWith(".css"))
            .Select(e => new StreamReader(e.Open()).ReadToEnd()));
        Assert.Contains("h2{color:#365f91 !important;", css);
    }

    [Fact]
    public async Task Export_MissingStyle_FailsForHtmlToo()
    {
        var result = await Export("= Doc\n\nText.\n", ExportFormat.Html, Path.Combine(_root, "gone.yml"));

        Assert.False(result.IsSuccess);
        Assert.Contains("gone.yml", result.ErrorMessage);
    }

    [Fact]
    public async Task Export_IncludeOutsideDocumentFolder_IsExpanded()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "shared.adoc"), "Shared copyright text.\n");

        var html = Encoding.UTF8.GetString(
            await ExportBytes("= Doc\n\ninclude::../../shared.adoc[]\n", ExportFormat.Html, null));

        Assert.Contains("Shared copyright text.", html);
    }

    private string WriteTheme(string yaml)
    {
        var path = Path.Combine(_root, $"t{Guid.NewGuid():N}-theme.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private async Task<ExportResult> Export(string source, ExportFormat format, string? theme)
    {
        var output = Path.Combine(_root, $"out-{Guid.NewGuid():N}.{format.Extension()}");
        return await new ExportService().ExportAsync(source, _docDir, output, format, ExportTheme.Asciidoctor, theme, null, CancellationToken.None);
    }

    private async Task<byte[]> ExportBytes(string source, ExportFormat format, string? theme, string? pdfTitle = null)
    {
        var output = Path.Combine(_root, $"out-{Guid.NewGuid():N}.{format.Extension()}");
        var result = await new ExportService().ExportAsync(source, _docDir, output, format, ExportTheme.Asciidoctor, theme, pdfTitle, CancellationToken.None);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return await File.ReadAllBytesAsync(output);
    }

    private static (double W, double H) MediaBox(byte[] pdf)
    {
        var m = Regex.Match(Encoding.Latin1.GetString(pdf), @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]");
        Assert.True(m.Success, "no /MediaBox in PDF");
        return (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string AnyTrueTypeFont()
    {
        string[] roots = [Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "/usr/share/fonts", "/System/Library/Fonts", "/Library/Fonts"];
        var font = roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.ttf", SearchOption.AllDirectories))
            .FirstOrDefault();
        Assert.True(font is not null, "test needs one installed TrueType font");
        return font!;
    }
}
