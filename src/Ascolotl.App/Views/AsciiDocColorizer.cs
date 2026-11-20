using Ascolotl.Core.Editing;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Ascolotl.App.Views;

/// <summary>Colours AsciiDoc source in the editor (see <see cref="AsciiDocSyntax"/>),
/// or Markdown source (see <see cref="MarkdownSyntax"/>).</summary>
public sealed class AsciiDocColorizer : DocumentColorizingTransformer
{
    private static readonly IReadOnlyDictionary<SyntaxKind, string> Light = new Dictionary<SyntaxKind, string>
    {
        [SyntaxKind.Heading] = "#C2416C",
        [SyntaxKind.Attribute] = "#A9732B",
        [SyntaxKind.BlockTitle] = "#7A6F99",
        [SyntaxKind.Delimiter] = "#A29E97",
        [SyntaxKind.Comment] = "#A29E97",
        [SyntaxKind.ListMarker] = "#D94F7A",
        [SyntaxKind.Admonition] = "#A9732B",
        [SyntaxKind.Macro] = "#3E6C9E",
        [SyntaxKind.Monospace] = "#8A5A44",
    };

    private static readonly IReadOnlyDictionary<SyntaxKind, string> Dark = new Dictionary<SyntaxKind, string>
    {
        [SyntaxKind.Heading] = "#F0889E",
        [SyntaxKind.Attribute] = "#D8A55E",
        [SyntaxKind.BlockTitle] = "#B6A6D8",
        [SyntaxKind.Delimiter] = "#7F7973",
        [SyntaxKind.Comment] = "#7F7973",
        [SyntaxKind.ListMarker] = "#E8608A",
        [SyntaxKind.Admonition] = "#D8A55E",
        [SyntaxKind.Macro] = "#86AEDB",
        [SyntaxKind.Monospace] = "#D4A48C",
    };

    private Dictionary<SyntaxKind, IBrush> _brushes = Brushes(Light);

    public void UseTheme(bool dark) => _brushes = Brushes(dark ? Dark : Light);

    public bool Markdown { get; set; }

    private static Dictionary<SyntaxKind, IBrush> Brushes(IReadOnlyDictionary<SyntaxKind, string> palette)
        => palette.ToDictionary(p => p.Key, p => (IBrush)new SolidColorBrush(Color.Parse(p.Value)));

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        foreach (var span in Markdown ? MarkdownSyntax.Classify(text) : AsciiDocSyntax.Classify(text))
        {
            var brush = _brushes[span.Kind];
            var emphasis = span.Kind is SyntaxKind.Heading or SyntaxKind.Admonition;
            var italic = span.Kind is SyntaxKind.Comment;
            ChangeLinePart(line.Offset + span.Start, line.Offset + span.Start + span.Length, element =>
            {
                element.TextRunProperties.SetForegroundBrush(brush);
                if (emphasis || italic)
                {
                    var face = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(face.FontFamily,
                        italic ? FontStyle.Italic : face.Style,
                        emphasis ? FontWeight.SemiBold : face.Weight));
                }
            });
        }
    }
}
