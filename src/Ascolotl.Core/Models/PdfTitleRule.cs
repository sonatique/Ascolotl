namespace Ascolotl.Core.Models;

/// <summary>How a PDF export's Title metadata is derived from the document title.</summary>
public enum PdfTitleRule
{
    /// <summary>The whole title, subtitle included (asciidoctor-pdf's behaviour).</summary>
    FullTitle,

    /// <summary>The title up to its first ':', dropping the subtitle (the default).</summary>
    UpToFirstColon,
}

public static class PdfTitles
{
    /// <summary>
    /// The PDF Title metadata for a document title: plain text (hard line
    /// breaks become spaces) with <paramref name="rule"/> applied; null when
    /// the document has no title.
    /// </summary>
    public static string? From(string? doctitle, PdfTitleRule rule)
    {
        if (string.IsNullOrWhiteSpace(doctitle))
            return null;

        var plain = doctitle.Replace(" +\n", " ").Replace("\n", " ").Trim();
        if (rule == PdfTitleRule.UpToFirstColon && plain.IndexOf(':') is > 0 and var colon)
            plain = plain[..colon].TrimEnd();
        return plain;
    }
}
