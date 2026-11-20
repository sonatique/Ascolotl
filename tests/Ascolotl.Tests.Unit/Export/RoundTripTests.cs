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

    private const string LiteralNestedTables = "Cvyqypzogj Hmwhj Vppqirkz Ymqqnf.adoc";
    private const string LiteralNestedTablesReason =
        "its nested tables sit in m! (monospace literal) cells, so their rows and titles are literal text, as in Asciidoctor";

    /// <summary>
    /// Documents whose export does not show some content, so no import can bring it
    /// back. Each still imports; only the comparison is waived.
    /// </summary>
    private static readonly Dictionary<(string Stage, string File), string> NotShownByExport = new()
    {
        [("first generation", LiteralNestedTables)] = LiteralNestedTablesReason,
        [("second generation", LiteralNestedTables)] = LiteralNestedTablesReason,
        [("from PDF", LiteralNestedTables)] = LiteralNestedTablesReason,
        [("HTML then PDF", LiteralNestedTables)] = LiteralNestedTablesReason,
    };

    /// <summary>
    /// .adoc to HTML, imported, exported to HTML again and imported again: both
    /// generations must still hold the original's titles, words and tables.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Html_ImportsBack_AndSurvivesASecondGeneration(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesPath, fileName));
        var reference = File.ReadAllText(await Export(source, ExportFormat.Html, "reference.html"));

        var first = await Import(ImportFormat.Html, Path.Combine(_dir, "reference.html"), "first");
        AssertMatches(reference, first, "first generation", fileName);
        var second = await Import(ImportFormat.Html, await Export(first, ExportFormat.Html, "first.html"), "second");
        AssertMatches(reference, second, "second generation", fileName);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Pdf_ImportsBack_AlsoAfterAnHtmlGeneration(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(ExamplesPath, fileName));
        var reference = File.ReadAllText(await Export(source, ExportFormat.Html, "reference.html"));

        AssertMatches(reference, await Import(ImportFormat.Pdf, await Export(source, ExportFormat.Pdf, "doc.pdf"), "pdf"), "from PDF", fileName);
        var viaHtml = await Import(ImportFormat.Html, Path.Combine(_dir, "reference.html"), "html");
        AssertMatches(reference, await Import(ImportFormat.Pdf, await Export(viaHtml, ExportFormat.Pdf, "html.pdf"), "html-pdf"), "HTML then PDF", fileName);
    }

    private async Task<string> Import(ImportFormat format, string path, string name)
    {
        var adoc = Path.Combine(_dir, name + ".adoc");
        var outcome = await new DocumentImportService().ImportAsync(format, path, adoc, CancellationToken.None);
        Assert.True(outcome.IsSuccess, outcome.Exception?.ToString() ?? outcome.ErrorMessage);
        return File.ReadAllText(adoc);
    }

    /// <summary>The imported AsciiDoc against the HTML export of the original, which stands for what the reader saw.</summary>
    private void AssertMatches(string reference, string imported, string stage, string fileName)
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

        if (NotShownByExport.TryGetValue((stage, fileName), out var reason))
        {
            output.WriteLine($"  not compared: {reason}");
            return;
        }
        Assert.True(lostTitles.Count == 0, $"{stage}: lost titles {string.Join(" | ", lostTitles)}");
        if (numberedTitles.Any(t => SectionNumber().IsMatch(t)))
            Assert.True(SettingLine(imported, "sectnums"), $"{stage}: numbered sections but no :sectnums:");
        if (reference.Contains("id=\"toc\"", StringComparison.Ordinal))
            Assert.True(SettingLine(imported, "toc"), $"{stage}: a table of contents but no :toc:");
        Assert.True(recall >= 0.95, $"{stage}: only {recall:P1} of the words came back");
        Assert.True(tables == importedTables, $"{stage}: {importedTables} of {tables} tables");
        AssertCodeLinesWhole(CodeLines(PreBlock().Matches(Body(reference)).Select(m => Text(m.Groups[1].Value, ""))), imported, stage);
    }

    /// <summary>
    /// A wide code line wraps in a PDF and a long block crosses a page; the import
    /// must give back each line whole and the block in one piece, or a copied
    /// example no longer works.
    /// </summary>
    [Fact]
    public async Task Pdf_KeepsWrappedCodeLinesAndPageSpanningBlocksWhole()
    {
        var longLine = "Item time=3000000 accesscode=\"56 34 12\" rawencoding=fec_decoded rfchannel=20 rssi=-50 clock=0 rawdata=\"00 06 01 02 03 04 05 06\"";
        var block = string.Join("\n", Enumerable.Range(1, 90).Select(i => $"Item time={i * 1000} rfchannel={i % 40}"));
        var source = $"= Code\n\n== Wide line\n\n----\n{longLine}\n----\n\n== Long block\n\n----\n{block}\n----\n";

        var imported = await Import(ImportFormat.Pdf, await Export(source, ExportFormat.Pdf, "code.pdf"), "code");

        Assert.Equal(2, CodeBlock().Matches(imported).Count);
        AssertCodeLinesWhole(CodeLines(CodeBlock().Matches(source).Select(m => m.Groups[2].Value)), imported, "from PDF");
    }

    private void AssertCodeLinesWhole(List<string> expected, string imported, string stage)
    {
        var importedLines = CodeLines(CodeBlock().Matches(imported).Select(m => m.Groups[2].Value)).ToHashSet();
        var broken = expected.Where(l => !importedLines.Contains(l)).ToList();
        output.WriteLine($"{stage}: code lines {expected.Count - broken.Count}/{expected.Count}");
        foreach (var l in broken.Take(10))
            output.WriteLine($"  code line not whole: {l}");
        Assert.True(broken.Count == 0, $"{stage}: {broken.Count} of {expected.Count} code lines did not come back whole");
    }

    /// <summary>Non-blank lines of the given code blocks, trimmed: indentation may differ.</summary>
    private static List<string> CodeLines(IEnumerable<string> blocks) =>
        [.. blocks.SelectMany(b => b.Split('\n')).Select(l => l.Trim()).Where(l => l.Length > 0)];

    private async Task<string> Export(string source, ExportFormat format, string name)
    {
        var path = Path.Combine(_dir, name);
        var result = await new ExportService().ExportAsync(source, false, ExamplesPath, path, format,
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
    [GeneratedRegex(@"<pre\b[^>]*>(.*?)</pre>", RegexOptions.Singleline)] private static partial Regex PreBlock();
    [GeneratedRegex(@"^(-{4,}|\.{4,})[ \t]*\r?\n(.*?)^\1[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.Singleline)] private static partial Regex CodeBlock();
    [GeneratedRegex(@"\s+")] private static partial Regex Space();
    [GeneratedRegex(@"^(\d+\.)+ ")] private static partial Regex SectionNumber();
    [GeneratedRegex(@"[*_`#]")] private static partial Regex Emphasis();
    [GeneratedRegex(@"[\p{L}\p{N}]{4,}")] private static partial Regex Word();
}
