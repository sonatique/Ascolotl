using System.Net;
using System.Text.RegularExpressions;
using Ascolotl.Core.Models;
using Ascolotl.Export;
using Xunit.Abstractions;

namespace Ascolotl.Tests.Unit.Export;

/// <summary>
/// Real documents through full round trips: each example .adoc is exported,
/// imported back, possibly again, and compared with its HTML export. Markup does
/// not survive a PDF, so the comparison is on what must: section titles, the
/// words, and the tables.
/// </summary>
public sealed partial class RoundTripTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string ExamplesPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "example-documents"));

    private readonly string _dir = Directory.CreateTempSubdirectory("ascolotl-roundtrip-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    public static IEnumerable<object[]> Examples() =>
        Directory.Exists(ExamplesPath)
            ? Directory.GetFiles(ExamplesPath, "*.adoc").Select(f => new object[] { Path.GetFileName(f) })
            : [];

    /// <summary>
    /// .adoc to HTML, imported, exported to HTML again and imported again: both
    /// generations must still hold the original's titles, words and tables.
    /// </summary>
    // Remove Skip once AdocNet's HtmlImporter passes: 1.0.31 bakes section and table
    // numbers into titles, turns the TOC into a list and admonitions into tables.
    [Theory(Skip = "AdocNet 1.0.31 HtmlImporter: numbered titles, TOC list, admonition tables")]
    [MemberData(nameof(Examples))]
    public async Task Html_ImportsBack_AndSurvivesASecondGeneration(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesPath, fileName));
        var reference = File.ReadAllText(await Export(source, ExportFormat.Html, "reference.html"));

        var first = await Import(ImportFormat.Html, Path.Combine(_dir, "reference.html"), "first");
        AssertMatches(reference, first, "first generation");
        var second = await Import(ImportFormat.Html, await Export(first, ExportFormat.Html, "first.html"), "second");
        AssertMatches(reference, second, "second generation");
    }

    // Remove Skip once AdocNet's PdfImporter passes: 1.0.31 crashes in PdfStructure.Join
    // on 4 of these documents and loses tables (and a few titles) on most others.
    [Theory(Skip = "AdocNet 1.0.31 PdfImporter: Join crash, lost tables")]
    [MemberData(nameof(Examples))]
    public async Task Pdf_ImportsBack_AlsoAfterAnHtmlGeneration(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesPath, fileName));
        var reference = File.ReadAllText(await Export(source, ExportFormat.Html, "reference.html"));

        AssertMatches(reference, await Import(ImportFormat.Pdf, await Export(source, ExportFormat.Pdf, "doc.pdf"), "pdf"), "from PDF");
        var viaHtml = await Import(ImportFormat.Html, Path.Combine(_dir, "reference.html"), "html");
        AssertMatches(reference, await Import(ImportFormat.Pdf, await Export(viaHtml, ExportFormat.Pdf, "html.pdf"), "html-pdf"), "HTML then PDF");
    }

    private async Task<string> Import(ImportFormat format, string path, string name)
    {
        var adoc = Path.Combine(_dir, name + ".adoc");
        var outcome = await new DocumentImportService().ImportAsync(format, path, adoc, CancellationToken.None);
        Assert.True(outcome.IsSuccess, outcome.Exception?.ToString() ?? outcome.ErrorMessage);
        return File.ReadAllText(adoc);
    }

    /// <summary>The imported AsciiDoc against the HTML export of the original, which stands for what the reader saw.</summary>
    private void AssertMatches(string reference, string imported, string stage)
    {
        // Inline tags join without a space: <strong>Name</strong>- x reads "Name- x".
        var numberedTitles = HtmlHeading().Matches(reference).Select(m => Normalize(Text(m.Groups[1].Value, ""))).Where(t => t.Length > 0).ToList();
        // The HTML numbers sections itself; the import must say :sectnums:, not bake "4.1." into each title.
        var titles = numberedTitles.Select(t => SectionNumber().Replace(t, "")).ToList();
        var importedHeadings = HeadingLine().Matches(imported).Select(m => Normalize(m.Groups[1].Value)).ToHashSet();
        var lostTitles = titles.Where(t => !importedHeadings.Contains(t)).ToList();

        var referenceWords = Words(Text(Body(reference)));
        var importedWords = Words(imported);
        var recall = referenceWords.Count == 0 ? 1 : referenceWords.Count(importedWords.Contains) / (double)referenceWords.Count;

        var tables = TableBlock().Count(reference);
        var importedTables = imported.Split('\n').Count(l => l.TrimEnd() == "|===") / 2;

        output.WriteLine($"{stage}: titles {titles.Count - lostTitles.Count}/{titles.Count}, words {recall:P1}, tables {importedTables}/{tables}");
        output.WriteLine("  missing words: " + string.Join(" ", referenceWords.Where(w => !importedWords.Contains(w)).Take(40)));
        foreach (var t in lostTitles)
            output.WriteLine($"  lost title: {t}");

        Assert.True(lostTitles.Count == 0, $"{stage}: lost titles {string.Join(" | ", lostTitles)}");
        if (numberedTitles.Any(t => SectionNumber().IsMatch(t)))
            Assert.True(SettingLine(imported, "sectnums"), $"{stage}: numbered sections but no :sectnums:");
        if (reference.Contains("id=\"toc\"", StringComparison.Ordinal))
            Assert.True(SettingLine(imported, "toc"), $"{stage}: a table of contents but no :toc:");
        Assert.True(recall >= 0.95, $"{stage}: only {recall:P1} of the words came back");
        Assert.True(tables == importedTables, $"{stage}: {importedTables} of {tables} tables");
    }

    private async Task<string> Export(string source, ExportFormat format, string name)
    {
        var path = Path.Combine(_dir, name);
        var result = await new ExportService().ExportAsync(source, ExamplesPath, path, format,
            ExportTheme.Asciidoctor, null, null, CancellationToken.None);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return path;
    }

    private static string Body(string html)
    {
        // The HTML-only chrome (TOC title, "Last updated" footer) is not in the PDF.
        html = HtmlOnly().Replace(ScriptOrStyle().Replace(html, " "), " ");
        var start = html.IndexOf("<body", StringComparison.Ordinal);
        return start < 0 ? html : html[start..];
    }

    private static string Text(string html, string tagGap = " ") => WebUtility.HtmlDecode(Tag().Replace(html, tagGap));

    private static bool SettingLine(string adoc, string name) =>
        adoc.Split('\n').Any(l => l.TrimEnd().StartsWith($":{name}:", StringComparison.Ordinal));

    /// <summary>Whitespace collapsed and emphasis marks dropped (an imported title keeps *bold*).</summary>
    private static string Normalize(string s) => Space().Replace(Emphasis().Replace(s, ""), " ").Trim();

    private static HashSet<string> Words(string text) =>
        Word().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToHashSet();

    [GeneratedRegex(@"^=+ +(.+)$", RegexOptions.Multiline)] private static partial Regex HeadingLine();
    [GeneratedRegex(@"<h[1-6][^>]*>(.*?)</h[1-6]>", RegexOptions.Singleline)] private static partial Regex HtmlHeading();
    [GeneratedRegex(@"<table\b[^>]*\btableblock\b")] private static partial Regex TableBlock();
    [GeneratedRegex(@"<(script|style)\b.*?</\1>", RegexOptions.Singleline)] private static partial Regex ScriptOrStyle();
    [GeneratedRegex(@"<div id=""(toctitle|footer-text)"">.*?</div>", RegexOptions.Singleline)] private static partial Regex HtmlOnly();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex(@"\s+")] private static partial Regex Space();
    [GeneratedRegex(@"^(\d+\.)+ ")] private static partial Regex SectionNumber();
    [GeneratedRegex(@"[*_`#]")] private static partial Regex Emphasis();
    [GeneratedRegex(@"[\p{L}\p{N}]{4,}")] private static partial Regex Word();
}
