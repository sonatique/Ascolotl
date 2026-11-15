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
    [InlineData(@"\\corp\docs\shared\x.adoc", false)] // same server as the document
    [InlineData(@"C:\docs\x.adoc", false)]
    [InlineData("/home/me/shared.adoc", false)]
    public void IsBlocked(string path, bool blocked)
        => Assert.Equal(blocked, LocalIncludeReader.IsBlocked(path, @"\\corp\docs\guide"));

    [Fact]
    public async Task Preview_NetworkInclude_IsNotReadAndIsReported()
    {
        var result = await new AdocRenderService().RenderAsync(
            "= Doc\n\ninclude::\\\\attacker\\share\\x.adoc[]\n", Path.GetTempPath(), false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Diagnostics!, d => d.Message.Contains("network and device paths are not read"));
    }
}
