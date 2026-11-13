using System.Text;
using System.Text.RegularExpressions;

namespace Ascolotl.Core.Editing;

/// <summary>One page of an exported website.</summary>
/// <param name="RelativeHtmlPath">Output path relative to the site root, using '/'.</param>
/// <param name="Title">Human title (document title or file name).</param>
public readonly record struct SitePage(string RelativeHtmlPath, string Title);

/// <summary>
/// Pure helpers for exporting a project folder as a static HTML site: path
/// mapping, inter-document link rewriting, title extraction, and the index
/// page. The I/O and AdocNet rendering live in the export service; everything
/// here is unit-testable.
/// </summary>
public static partial class WebsiteBuilder
{
    [GeneratedRegex("""(href\s*=\s*")([^"#:]+)\.(adoc|asciidoc)(#[^"]*)?(")""", RegexOptions.IgnoreCase)]
    private static partial Regex DocHrefRegex();

    /// <summary>Map a source-relative document path to its output path
    /// (same folders, .html extension, '/' separators).</summary>
    public static string ToHtmlPath(string relativeDocPath)
    {
        var normalized = relativeDocPath.Replace('\\', '/');
        var dot = normalized.LastIndexOf('.');
        return (dot < 0 ? normalized : normalized[..dot]) + ".html";
    }

    /// <summary>
    /// Rewrite links between exported documents: <c>href="…​.adoc"</c> (or
    /// .asciidoc), with or without a <c>#anchor</c>, becomes the .html
    /// equivalent. External URLs (scheme present) are left alone.
    /// </summary>
    public static string RewriteDocLinks(string html) =>
        DocHrefRegex().Replace(html, m =>
            m.Groups[1].Value + m.Groups[2].Value + ".html" + m.Groups[4].Value + m.Groups[5].Value);

    /// <summary>Document title: the first <c>= Title</c> line, else the file
    /// name without extension.</summary>
    public static string ExtractTitle(string source, string fallbackFileName)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var t = line.TrimStart();
            if (t.Length == 0 || t.StartsWith("//", StringComparison.Ordinal)) continue;
            if (t.StartsWith("= ", StringComparison.Ordinal))
                return t[2..].Trim();
            // First non-empty, non-comment line that isn't a doc title — the
            // document has no title line (attribute entries may precede it).
            if (!t.StartsWith(':')) break;
        }
        return Path.GetFileNameWithoutExtension(fallbackFileName);
    }

    /// <summary>True when the project provides its own site index (an
    /// index.adoc at the root), in which case no index is generated.</summary>
    public static bool HasOwnIndex(IEnumerable<string> relativeDocPaths) =>
        relativeDocPaths.Any(p =>
        {
            var n = p.Replace('\\', '/');
            return n.Equals("index.adoc", StringComparison.OrdinalIgnoreCase)
                || n.Equals("index.asciidoc", StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>
    /// Generate the site index: a minimal, dependency-free HTML page listing
    /// every exported page grouped by folder, in the order given.
    /// </summary>
    public static string BuildIndexHtml(string siteTitle, IReadOnlyList<SitePage> pages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Escape(siteTitle)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:-apple-system,'Segoe UI',sans-serif;max-width:640px;margin:3rem auto;padding:0 1rem;color:#2d2a26;background:#faf6f1}");
        sb.AppendLine("h1{font-size:1.5rem} h2{font-size:.8rem;text-transform:uppercase;letter-spacing:.06em;color:#7a7168;margin-top:1.6rem}");
        sb.AppendLine("ul{list-style:none;padding:0;margin:.3rem 0} li{margin:.25rem 0}");
        sb.AppendLine("a{color:#b03a5e;text-decoration:none} a:hover{text-decoration:underline}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>{Escape(siteTitle)}</h1>");

        string? currentFolder = null;
        var open = false;
        foreach (var page in pages)
        {
            var slash = page.RelativeHtmlPath.LastIndexOf('/');
            var folder = slash < 0 ? string.Empty : page.RelativeHtmlPath[..slash];
            if (folder != currentFolder)
            {
                if (open) sb.AppendLine("</ul>");
                if (folder.Length > 0)
                    sb.AppendLine($"<h2>{Escape(folder)}</h2>");
                sb.AppendLine("<ul>");
                open = true;
                currentFolder = folder;
            }
            else if (!open)
            {
                sb.AppendLine("<ul>");
                open = true;
            }
            sb.AppendLine($"<li><a href=\"{page.RelativeHtmlPath}\">{Escape(page.Title)}</a></li>");
        }
        if (open) sb.AppendLine("</ul>");

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
