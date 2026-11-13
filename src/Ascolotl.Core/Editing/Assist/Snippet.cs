using System.Text;

namespace Ascolotl.Core.Editing.Assist;

/// <summary>A tab-stop in an expanded snippet.</summary>
/// <param name="Index">The stop number (<c>$1</c>, <c>$2</c>, …); <c>0</c> is the
/// final caret position, navigated to last.</param>
/// <param name="Start">Offset of the placeholder within the expanded text.</param>
/// <param name="Length">Length of the placeholder text (0 for a bare stop).</param>
/// <param name="Placeholder">The placeholder text inserted for this stop.</param>
public sealed record SnippetStop(int Index, int Start, int Length, string Placeholder);

/// <summary>The plain expanded text plus its ordered tab-stops.</summary>
public sealed record ParsedSnippet(string Text, IReadOnlyList<SnippetStop> Stops);

/// <summary>
/// Parses a snippet template into plain text plus ordered tab-stops. Supports
/// <c>${n:placeholder}</c>, <c>${n}</c>, and shorthand <c>$n</c> (including
/// <c>$0</c> for the final caret). Anything else is literal. Stops are returned
/// ordered by number ascending, with <c>$0</c> last.
/// </summary>
public static class Snippet
{
    public static ParsedSnippet Parse(string template)
    {
        var sb = new StringBuilder();
        var stops = new List<SnippetStop>();
        var i = 0;

        while (i < template.Length)
        {
            var c = template[i];
            if (c == '$' && i + 1 < template.Length)
            {
                var n = template[i + 1];
                if (n == '{')
                {
                    var j = i + 2;
                    var num = 0;
                    var hasNum = false;
                    while (j < template.Length && char.IsDigit(template[j]))
                    {
                        num = num * 10 + (template[j] - '0');
                        j++;
                        hasNum = true;
                    }

                    if (hasNum && j < template.Length && (template[j] == ':' || template[j] == '}'))
                    {
                        var placeholder = string.Empty;
                        if (template[j] == ':')
                        {
                            var k = j + 1;
                            while (k < template.Length && template[k] != '}') k++;
                            placeholder = template[(j + 1)..k];
                            j = k;
                        }

                        if (j < template.Length && template[j] == '}')
                        {
                            var start = sb.Length;
                            sb.Append(placeholder);
                            stops.Add(new SnippetStop(num, start, placeholder.Length, placeholder));
                            i = j + 1;
                            continue;
                        }
                    }
                    // Not a well-formed stop — emit literally.
                    sb.Append(c);
                    i++;
                    continue;
                }

                if (char.IsDigit(n))
                {
                    var j = i + 1;
                    var num = 0;
                    while (j < template.Length && char.IsDigit(template[j]))
                    {
                        num = num * 10 + (template[j] - '0');
                        j++;
                    }
                    stops.Add(new SnippetStop(num, sb.Length, 0, string.Empty));
                    i = j;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        // $1, $2, … in order; $0 (final caret) last.
        var ordered = stops
            .OrderBy(s => s.Index == 0 ? int.MaxValue : s.Index)
            .ToList();
        return new ParsedSnippet(sb.ToString(), ordered);
    }
}
