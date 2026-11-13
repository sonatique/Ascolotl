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
    public async Task Export_EmptyProject_Fails()
    {
        var result = await new WebsiteExportService().ExportAsync(
            _root, [], _out, CancellationToken.None);
        Assert.False(result.IsSuccess);
    }
}
