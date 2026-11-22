namespace Ascolotl.Tests.Unit.Services;

using Ascolotl.Core.Services;

public sealed class LocalIncludeReaderTests
{
    [Theory]
    [InlineData(@"\\attacker\share\x.adoc", true)]
    [InlineData("//attacker/share/x.adoc", true)]
    [InlineData(@"\\?\C:\docs\x.adoc", true)]
    [InlineData(@"\\.\PhysicalDrive0", true)]
    [InlineData("/dev/zero", true)]
    [InlineData("/proc/self/environ", true)]
    [InlineData(@"\??\UNC\attacker\share\x.yml", true)]
    [InlineData(@"\??\GLOBALROOT\Device\Mup\attacker\share\x.yml", true)]
    [InlineData(@"\\corp\docs\shared\x.adoc", false)] // same server as the document
    [InlineData(@"C:\docs\x.adoc", false)]
    [InlineData("/home/me/shared.adoc", false)]
    public void IsBlocked(string path, bool blocked)
        => Assert.Equal(blocked, LocalIncludeReader.IsBlocked(path, @"\\corp\docs\guide"));

    [Fact]
    public void IsBlocked_DotDotIntoDevices_OnUnix()
    {
        if (OperatingSystem.IsWindows())
            return; // /dev is an ordinary folder name there
        Assert.True(LocalIncludeReader.IsBlocked("/home/u/docs/../../../dev/zero", "/home/u/docs"));
    }

    [Fact]
    public void IncludeRoot_IsTheOpenFolder_ElseTheRepository_ElseTheDocumentFolder()
    {
        var top = Directory.CreateTempSubdirectory("ascolotl-root-").FullName;
        try
        {
            var docs = Directory.CreateDirectory(Path.Combine(top, "repo", "docs")).FullName;
            Assert.Equal(docs, LocalIncludeReader.IncludeRoot(docs, null));
            Directory.CreateDirectory(Path.Combine(top, "repo", ".git"));
            Assert.Equal(Path.Combine(top, "repo"), LocalIncludeReader.IncludeRoot(docs, null));
            Assert.Equal(top, LocalIncludeReader.IncludeRoot(docs, top));
            Assert.Equal(Path.Combine(top, "repo"), LocalIncludeReader.IncludeRoot(docs, Path.GetTempPath() + "elsewhere"));
            Assert.Null(LocalIncludeReader.IncludeRoot(null, top)); // unsaved: the user's own typing
        }
        finally
        {
            Directory.Delete(top, recursive: true);
        }
    }

    [Fact]
    public void IncludesLeavingTheRepository_AreNotRead_EvenThroughALink()
    {
        var top = Directory.CreateTempSubdirectory("ascolotl-root-").FullName;
        try
        {
            var repo = Directory.CreateDirectory(Path.Combine(top, "repo")).FullName;
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            File.WriteAllText(Path.Combine(top, "secret.txt"), "SECRET");
            File.WriteAllText(Path.Combine(repo, "shared.adoc"), "SHARED");
            var source = $"= Doc\n\ninclude::shared.adoc[]\n\ninclude::../secret.txt[]\n\ninclude::{Path.Combine(top, "secret.txt")}[]\n";
            try
            {
                File.CreateSymbolicLink(Path.Combine(repo, "link.txt"), Path.Combine(top, "secret.txt"));
                source += "\ninclude::link.txt[]\n";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Windows without Developer Mode

            var parsed = DocumentParser.Parse(source, false, repo);
            using var output = new MemoryStream();
            new AdocNet.Converters.Html.HtmlRenderer().Render(parsed.Document, output, new AdocNet.Converters.Html.HtmlRenderOptions());
            var text = System.Text.Encoding.UTF8.GetString(output.ToArray());

            Assert.Contains("SHARED", text);
            Assert.DoesNotContain("SECRET", text);
            Assert.Contains(parsed.Diagnostics, d => d.Message.Contains("outside the project"));
        }
        finally
        {
            Directory.Delete(top, recursive: true);
        }
    }

    [Fact]
    public async Task IncludesThatMultiply_StopAtTheBudget_AndSaySo()
    {
        // f0 includes f1 four times, f1 includes f2 four times...: 4^10 reads unchecked.
        var dir = Directory.CreateTempSubdirectory("ascolotl-fanout-").FullName;
        try
        {
            for (var i = 0; i < 10; i++)
                File.WriteAllText(Path.Combine(dir, $"f{i}.adoc"),
                    i == 9 ? "leaf\n" : string.Concat(Enumerable.Repeat($"include::f{i + 1}.adoc[]\n\n", 4)));
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var result = await new AdocRenderService().RenderAsync(
                "= Doc\n\ninclude::f0.adoc[]\n", dir, markdown: false, standalone: false, CancellationToken.None);

            Assert.InRange(watch.Elapsed.TotalSeconds, 0, 20);
            Assert.Contains(result.Diagnostics!, d => d.Message.Contains($"more than {LocalIncludeReader.MaxReads} files"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_NetworkInclude_IsNotReadAndIsReported()
    {
        if (!OperatingSystem.IsWindows())
            return; // \\server\share is a file name elsewhere
        var result = await new AdocRenderService().RenderAsync(
            "= Doc\n\ninclude::\\\\attacker\\share\\x.adoc[]\n", Path.GetTempPath(), markdown: false, standalone: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Diagnostics!, d => d.Message.Contains("network and device paths are not read"));
    }
}
