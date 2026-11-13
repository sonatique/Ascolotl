namespace Ascolotl.Core.Models;

/// <summary>
/// A visual style applied consistently across export formats — the same theme
/// choice drives both the HTML stylesheet and the PDF's colors, so one profile
/// yields matching output everywhere (unified styling).
/// </summary>
public enum ExportTheme
{
    /// <summary>Classic Asciidoctor look (the default).</summary>
    Asciidoctor,

    /// <summary>Minimal, neutral typography.</summary>
    Clean,

    /// <summary>GitHub-flavored: familiar headings, blue links, gray code.</summary>
    Github,
}

public static class ExportThemeExtensions
{
    public static string DisplayName(this ExportTheme theme) => theme switch
    {
        ExportTheme.Asciidoctor => "Asciidoctor",
        ExportTheme.Clean => "Clean",
        ExportTheme.Github => "GitHub",
        _ => theme.ToString(),
    };
}
