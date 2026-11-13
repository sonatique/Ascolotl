using AdocNet.Ast;
using AdocNet.Converters.Pdf;
using Ascolotl.Export;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;

namespace Ascolotl.App.Services;

/// <summary>
/// The styled preview's title page: a page-sized sheet laid out from the
/// theme's <c>title-page</c> keys (background, logo, title, subtitle, authors,
/// revision) with the same positions as the PDF export. Text metrics are
/// Avalonia's, so line breaks can differ slightly from the PDF.
/// </summary>
public static class PreviewTitlePage
{
    private const double Dip = PreviewStyle.DipPerPoint;

    /// <summary>The title sheet for <paramref name="document"/>; null when the theme defines no title page.</summary>
    public static Control? Build(PdfRenderOptions o, DocumentNode document)
    {
        if (o.TitlePage is not { } tp || document.Title is null)
            return null;

        double pageW = o.PageWidth * Dip, pageH = o.PageHeight * Dip;
        double left = o.MarginLeft * Dip, contentW = (o.PageWidth - o.MarginLeft - o.MarginRight) * Dip;
        var sheet = new Canvas { Width = pageW, Height = pageH, Background = Brushes.White, ClipToBounds = true };

        if (tp.BackgroundImagePath is { } bg && LoadImage(bg) is { } background)
            sheet.Children.Add(new Image { Source = background, Width = pageW, Height = pageH, Stretch = Stretch.Fill });

        if (tp.LogoImagePath is { } logoPath && LoadImage(logoPath) is { } logo && logo.Size.Width > 0)
        {
            double width = Math.Min(tp.LogoWidth is { } lw ? lw * Dip : logo.Size.Width, contentW);
            double height = width * logo.Size.Height / logo.Size.Width;
            double x = (tp.LogoAlignment ?? tp.Alignment) switch
            {
                PdfAlignment.Right => left + contentW - width,
                PdfAlignment.Center => left + (contentW - width) / 2,
                _ => left,
            };
            var image = new Image { Source = logo, Width = width, Height = height, Stretch = Stretch.Uniform };
            Canvas.SetLeft(image, x);
            Canvas.SetTop(image, TitlePageLayout.ResolveTop(tp.LogoTop, o) * Dip);
            sheet.Children.Add(image);
        }

        var (main, subtitle) = TitlePageLayout.Partition(document.Title, document.Attributes);
        var blocks = new StackPanel { Width = contentW };
        blocks.Children.Add(Text(main, tp.Title, tp.Alignment, o));
        if (subtitle is not null)
            blocks.Children.Add(Text(subtitle, tp.Subtitle, tp.Alignment, o));
        if (TitlePageLayout.AuthorLine(document.Attributes) is { } authors)
            blocks.Children.Add(Text(authors, tp.Authors, tp.Alignment, o));
        if (TitlePageLayout.RevisionLine(document.Attributes) is { } revision)
            blocks.Children.Add(Text(revision, tp.Revision, tp.Alignment, o));

        Canvas.SetLeft(blocks, left);
        Canvas.SetTop(blocks, (tp.Title.Top is { } top ? TitlePageLayout.ResolveTop(top, o) : o.MarginTop) * Dip);
        sheet.Children.Add(blocks);
        return sheet;
    }

    private static TextBlock Text(string text, PdfTitlePageText style, PdfAlignment alignment, PdfRenderOptions o)
    {
        double size = (style.FontSize ?? o.FontSize) * Dip;
        var block = new TextBlock
        {
            Text = TitlePageLayout.TitlePageText(text),
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            LineHeight = size * (style.LineHeight ?? o.LineSpacing),
            FontWeight = style.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = PreviewStyle.Brush(style.Color ?? o.BodyColor) ?? Brushes.Black,
            Margin = new Thickness(0, style.MarginTop * Dip, 0, style.MarginBottom * Dip),
            TextAlignment = alignment switch
            {
                PdfAlignment.Right => TextAlignment.Right,
                PdfAlignment.Center => TextAlignment.Center,
                _ => TextAlignment.Left,
            },
        };
        if (PreviewStyle.Font(style.FontPath ?? o.FontPath) is { } font)
            block.FontFamily = font;
        return block;
    }

    /// <summary>SVG, PNG or JPEG as an Avalonia image; null when missing or unreadable.</summary>
    private static IImage? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return new SvgImage { Source = SvgSource.Load(path, null, null) };
            return new Bitmap(path);
        }
        catch (Exception)
        {
            // A broken image must not take the preview down; the export reports it.
            return null;
        }
    }
}
