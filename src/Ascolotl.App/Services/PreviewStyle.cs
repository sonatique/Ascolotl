using AdocNet.Avalonia;
using AdocNet.Converters.Pdf;
using Ascolotl.Export;
using Avalonia.Media;

namespace Ascolotl.App.Services;

/// <summary>
/// Maps the active style (asciidoctor-pdf theme) onto the live preview, so
/// Split and WYSIWYG show the colours, sizes and text width of the export.
/// </summary>
public static class PreviewStyle
{
    /// <summary>Theme sizes are PDF points; Avalonia lays out in 1/96 inch.</summary>
    public const double DipPerPoint = 96.0 / 72.0;

    private static (string Path, DateTime Stamp, PdfRenderOptions Options)? _cache;

    /// <summary>
    /// The style for a document (its :pdf-theme:, else the user's default),
    /// or null for the built-in look. Cached until the theme file changes, so
    /// editing the YAML shows up on the next render.
    /// </summary>
    public static PdfRenderOptions? Load(
        string? defaultStylePath, IReadOnlyDictionary<string, string> attributes, string docDir)
    {
        try
        {
            var path = PdfThemeResolver.ResolveThemePath(attributes, docDir, defaultStylePath);
            if (path is null || !File.Exists(path))
                return null;

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_cache is { } c && c.Path == path && c.Stamp == stamp)
                return c.Options;

            var options = PdfThemeResolver.Load(path, attributes, docDir);
            _cache = (path, stamp, options);
            return options;
        }
        catch (Exception)
        {
            // A broken theme must not take the preview down; export reports it.
            return null;
        }
    }

    public static AvaloniaRenderTheme ThemeFor(PdfRenderOptions o)
    {
        var theme = new AvaloniaRenderTheme
        {
            BodyFont = Font(o.FontPath),
            HeadingFont = Font(o.HeadingFontPath),
            BodyForeground = Brush(o.BodyColor),
            FallbackHeadingForeground = Brush(o.HeadingColor),
            HeadingForegrounds = [Brush(o.Heading2Color), Brush(o.Heading3Color), Brush(o.Heading4Color), Brush(o.Heading5Color)],
            TableHeaderForeground = Brush(o.TableHeaderFontColor),
            DocumentTitleFontSize = o.TitleFontSize * DipPerPoint,
            BodyFontSize = o.FontSize * DipPerPoint,
            HeadingFontSizes = [.. new[] { o.Heading2FontSize, o.Heading3FontSize, o.Heading4FontSize, o.Heading5FontSize }
                .Select(s => (s ?? o.FontSize) * DipPerPoint)],
        };
        if (Font(o.MonoFontPath) is { } mono) theme.MonospaceFont = mono;
        if (Brush(o.LinkColor) is { } link) theme.LinkBrush = link;
        if (Brush(o.CodeBackground) is { } code) theme.CodeBlockBackground = code;
        if (Brush(o.TableBorderColor) is { } border) theme.TableBorderBrush = border;
        if (Brush(o.TableHeaderBackground) is { } header) theme.TableHeaderBackground = header;
        return theme;
    }

    /// <summary>The theme's font as an Avalonia family, by name; null when unreadable.</summary>
    // ponytail: Avalonia 11.3 cannot load file:// font URIs ("Could not create
    // glyphTypeface"), so this names the family: right for installed fonts, falls
    // back to the default font otherwise. Register a font collection if themes ship
    // their own uninstalled fonts.
    public static FontFamily? Font(string? path)
        => !string.IsNullOrEmpty(path) && TrueTypeFontInfo.ReadFamilyName(path) is { } name
            ? new FontFamily(name)
            : null;

    public static IBrush? Brush(PdfColor? color)
        => color is { } c ? new SolidColorBrush(Color.FromRgb(Byte(c.R), Byte(c.G), Byte(c.B))) : null;

    private static byte Byte(float channel) => (byte)Math.Round(Math.Clamp(channel, 0f, 1f) * 255);
}
