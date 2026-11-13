using AdocNet.Converters.Pdf;

namespace Ascolotl.Export;

/// <summary>
/// Picks and loads the asciidoctor-pdf YAML theme for a PDF export, following
/// asciidoctor-pdf's attribute rules so documents built by an
/// <c>asciidoctor-pdf --theme …</c> script render the same way here.
/// </summary>
public static class PdfThemeResolver
{
    /// <summary>
    /// Theme file to use, or null for AdocNet's built-in look. The document's
    /// <c>pdf-theme</c> (resolved against <c>pdf-themesdir</c>, else the
    /// document directory) wins over <paramref name="defaultThemePath"/>, the
    /// user's app-wide choice. <c>pdf-theme: default</c> or <c>base</c> forces
    /// the built-in look.
    /// </summary>
    public static string? ResolveThemePath(
        IReadOnlyDictionary<string, string> attributes, string docDir, string? defaultThemePath)
    {
        if (attributes.TryGetValue("pdf-theme", out var theme) && !string.IsNullOrWhiteSpace(theme))
        {
            theme = theme.Trim();
            if (theme is "default" or "base")
                return null;

            // asciidoctor-pdf: a value ending in .yml is a file; otherwise it
            // names <value>-theme.yml in the themes directory.
            var fileName = theme.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? theme : $"{theme}-theme.yml";
            var themesDir = attributes.TryGetValue("pdf-themesdir", out var dir) && !string.IsNullOrWhiteSpace(dir)
                ? Path.Combine(docDir, dir.Trim())
                : docDir;
            return Path.GetFullPath(Path.Combine(themesDir, fileName));
        }

        return string.IsNullOrWhiteSpace(defaultThemePath) ? null : defaultThemePath;
    }

    /// <summary>
    /// Loads <paramref name="themePath"/>. Theme font catalogs hold relative
    /// paths (e.g. <c>fonts/calibri.ttf</c>), and the build script usually
    /// supplies the root via <c>--attribute pdf-fontsdir=C:\Windows\</c>. So
    /// try the document's <c>pdf-fontsdir</c> entries, then the theme's own
    /// directory, then the OS fonts directory and its parent, and keep the
    /// first root where the theme's fonts actually exist.
    /// </summary>
    public static PdfRenderOptions Load(
        string themePath, IReadOnlyDictionary<string, string> attributes, string docDir)
    {
        PdfRenderOptions? first = null;
        foreach (var fontsDir in FontsDirCandidates(themePath, attributes, docDir))
        {
            var options = PdfThemeLoader.Load(themePath, fontsDir);
            // AdocNet drops catalog fonts it cannot find, leaving FontPath empty.
            if (!string.IsNullOrEmpty(options.FontPath))
                return options;
            first ??= options;
        }

        // No root holds the fonts (or the theme declares none): keep the
        // theme's colours and layout with the built-in fonts.
        return first ?? PdfThemeLoader.Load(themePath, null);
    }

    private static IEnumerable<string> FontsDirCandidates(
        string themePath, IReadOnlyDictionary<string, string> attributes, string docDir)
    {
        if (attributes.TryGetValue("pdf-fontsdir", out var fontsDirs))
        {
            // asciidoctor-pdf accepts a ;- or ,-separated list; GEM_FONTS_DIR
            // names its bundled fonts, which AdocNet does not ship.
            foreach (var entry in fontsDirs.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (entry != "GEM_FONTS_DIR")
                    yield return Path.GetFullPath(Path.Combine(docDir, entry));
            }
        }

        yield return Path.GetDirectoryName(Path.GetFullPath(themePath))!;

        var systemFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(systemFonts))
        {
            yield return systemFonts;
            if (Path.GetDirectoryName(systemFonts) is { } parent)
                yield return parent;
        }
    }
}
