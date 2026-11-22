namespace Ascolotl.Tests.Unit.Export;

using Ascolotl.Export;

/// <summary>
/// End-to-end tests for <see cref="WebsiteExportService"/> against a real
/// temp folder — pages rendered, links rewritten, index generated.
/// </summary>
public class WebsiteExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _out;

    public WebsiteExportServiceTests()
    {
        _root = Directory.CreateTempSubdirectory("ascolotl-site-src-").FullName;
        _out = Directory.CreateTempSubdirectory("ascolotl-site-out-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_out, recursive: true); } catch { }
    }

    [Fact]
    public async Task Export_RendersPages_RewritesLinks_GeneratesIndex()
    {
        File.WriteAllText(Path.Combine(_root, "readme.adoc"),
            "= Read Me\n\nSee link:docs/guide.adoc[the guide].\n");
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "docs", "guide.adoc"),
            "= User Guide\n\nContent.\n");

        var result = await new WebsiteExportService().ExportAsync(
            _root, ["readme.adoc", Path.Combine("docs", "guide.adoc")], _out, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var readme = File.ReadAllText(Path.Combine(_out, "readme.html"));
        Assert.Contains("docs/guide.html", readme);          // link rewritten
        Assert.DoesNotContain("guide.adoc", readme);
        Assert.True(File.Exists(Path.Combine(_out, "docs", "guide.html")));

        var index = File.ReadAllText(Path.Combine(_out, "index.html"));
        Assert.Contains("Read Me", index);
        Assert.Contains("User Guide", index);
    }

    [Fact]
    public async Task Export_DoesNotCopyWhatALinkPointsTo()
    {
        var secret = Path.Combine(_out, "..", $"secret-{Guid.NewGuid():N}.txt");
        File.WriteAllText(secret, "private key");
        try
        {
            Directory.CreateDirectory(Path.Combine(_root, "img"));
            try { File.CreateSymbolicLink(Path.Combine(_root, "img", "logo.png"), secret); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // Windows without Developer Mode
            File.WriteAllText(Path.Combine(_root, "guide.adoc"), "= Guide\n\nimage::img/logo.png[]\n");

            var result = await new WebsiteExportService().ExportAsync(_root, ["guide.adoc"], _out, CancellationToken.None);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(File.Exists(Path.Combine(_out, "img", "logo.png")));
        }
        finally
        {
            File.Delete(secret);
        }
    }

    [Fact]
    public async Task Export_PageWithAnImageOnADevicePath_IsRefused()
    {
        File.WriteAllText(Path.Combine(_root, "guide.adoc"), "= Guide\n\nimage::/dev/zero[]\n");

        var result = await new WebsiteExportService().ExportAsync(_root, ["guide.adoc"], _out, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("/dev/zero", result.ErrorMessage);
    }

    [Fact]
    public async Task Export_ProjectWithOwnIndex_DoesNotClobberIt()
    {
        File.WriteAllText(Path.Combine(_root, "index.adoc"), "= My Own Index\n\nHome.\n");

        var result = await new WebsiteExportService().ExportAsync(
            _root, ["index.adoc"], _out, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var index = File.ReadAllText(Path.Combine(_out, "index.html"));
        Assert.Contains("My Own Index", index); // rendered doc, not generated listing
    }

    [Fact]
    public async Task Export_StaleHtmlInProject_DoesNotReplaceRenderedPage()
    {
        File.WriteAllText(Path.Combine(_root, "guide.adoc"), "= Guide\n\nFresh text.\n");
        File.WriteAllText(Path.Combine(_root, "guide.html"), "stale");

        var result = await new WebsiteExportService().ExportAsync(_root, ["guide.adoc"], _out, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("Fresh text.", File.ReadAllText(Path.Combine(_out, "guide.html")));
    }

    [Fact]
    public async Task Export_CopiesImages_SkipsDotFoldersAndOutput()
    {
        File.WriteAllText(Path.Combine(_root, "a.adoc"), "= A\n\nimage::images/logo.png[]\n");
        Directory.CreateDirectory(Path.Combine(_root, "images"));
        File.WriteAllBytes(Path.Combine(_root, "images", "logo.png"), [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, ".git", "config"), "secret");
        var output = Path.Combine(_root, "site"); // output inside the project

        var result = await new WebsiteExportService().ExportAsync(_root, ["a.adoc"], output, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(output, "images", "logo.png")));
        Assert.False(Directory.Exists(Path.Combine(output, ".git")));
        Assert.False(Directory.Exists(Path.Combine(output, "site")));
        Assert.False(File.Exists(Path.Combine(output, "a.adoc")));
    }

    [Fact]
    public async Task Export_IncludeFromAnotherFolder_IsExpanded()
    {
        File.WriteAllText(Path.Combine(_root, "shared.adoc"), "Shared text.\n");
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "docs", "guide.adoc"), "= Guide\n\ninclude::../shared.adoc[]\n");

        var result = await new WebsiteExportService().ExportAsync(
            _root, [Path.Combine("docs", "guide.adoc")], _out, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("Shared text.", File.ReadAllText(Path.Combine(_out, "docs", "guide.html")));
    }

    [Fact]
    public async Task Export_TwoDocumentsWithSameOutputName_FailsWithoutWriting()
    {
        File.WriteAllText(Path.Combine(_root, "a.adoc"), "= From adoc\n");
        File.WriteAllText(Path.Combine(_root, "a.asciidoc"), "= From asciidoc\n");

        var result = await new WebsiteExportService().ExportAsync(
            _root, ["a.adoc", "a.asciidoc"], _out, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("a.adoc and a.asciidoc", result.ErrorMessage);
        Assert.False(File.Exists(Path.Combine(_out, "a.html")));
    }

    [Fact]
    public async Task Export_IndexUsesParsedTitle()
    {
        // A block comment before the title used to hide it from the index.
        File.WriteAllText(Path.Combine(_root, "a.adoc"), "////\nDraft notes\n////\n= Real Title\n\nText.\n");

        var result = await new WebsiteExportService().ExportAsync(_root, ["a.adoc"], _out, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(">Real Title</a>", File.ReadAllText(Path.Combine(_out, "index.html")));
    }

    [Fact]
    public async Task Export_EmptyProject_Fails()
    {
        var result = await new WebsiteExportService().ExportAsync(
            _root, [], _out, CancellationToken.None);
        Assert.False(result.IsSuccess);
    }
}
