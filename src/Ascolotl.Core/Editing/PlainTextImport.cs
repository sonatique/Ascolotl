using System.Text;
using System.Text.RegularExpressions;

namespace Ascolotl.Core.Editing;

/// <summary>
/// Turns a plain text file into AsciiDoc that reads the same: blank lines
/// separate paragraphs, "-", "*" and "•" lines become bullet lists, "1." and
/// "1)" lines numbered lists, indented blocks stay literal, and any other line
/// that AsciiDoc would read as markup is escaped.
/// </summary>
public static partial class PlainTextImport
{
    public static string ToAsciiDoc(string text, string title)
    {
        var sb = new StringBuilder();
        sb.Append("= ").Append(title.Trim()).Append("\n\n");

        bool? previousWasItem = null; // null: start, or after a blank line
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.Append('\n');
                previousWasItem = null;
                continue;
            }

            var bullet = Bullet().Match(line);
            var numbered = Numbered().Match(line);
            var isItem = bullet.Success || numbered.Success;
            // A list needs a blank line before it, and text right after an item
            // would join that item.
            if (previousWasItem is { } wasItem && wasItem != isItem)
                sb.Append('\n');
            previousWasItem = isItem;

            // Indented lines are a literal block in AsciiDoc already: keep them.
            if (char.IsWhiteSpace(line[0]))
                sb.Append(line.TrimEnd());
            else if (bullet.Success)
                sb.Append("* ").Append(Escape(bullet.Groups[1].Value));
            else if (numbered.Success)
                sb.Append(". ").Append(Escape(numbered.Groups[1].Value));
            else
                sb.Append(Escape(line.TrimEnd()));
            sb.Append('\n');
        }

        // Collapse runs of blank lines left by the source.
        return BlankRuns().Replace(sb.ToString(), "\n\n").TrimEnd() + "\n";
    }

    /// <summary>Prefixes a line AsciiDoc would read as a block marker so it stays text.</summary>
    private static string Escape(string line)
        => line.Length > 0 && "=*-.[|/:<>+#_'\"".Contains(line[0]) ? "{empty}" + line : line;

    [GeneratedRegex(@"^[-*•]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\d{1,3}[.)]\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();
}
