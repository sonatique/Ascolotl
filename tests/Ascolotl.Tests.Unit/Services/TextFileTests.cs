using System.Text;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class TextFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ascolotl-textfile-{Guid.NewGuid():N}");

    public TextFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* cleanup */ }
    }

    private async Task<byte[]> RoundTripAsync(byte[] original)
    {
        var path = Path.Combine(_dir, "doc.adoc");
        await File.WriteAllBytesAsync(path, original);
        var read = await TextFile.ReadAsync(path);
        await TextFile.WriteAsync(path, read.Text, TextFile.EncodingFor(read.Text, read.Encoding));
        return await File.ReadAllBytesAsync(path);
    }

    [Fact]
    public async Task WriteAsync_ReadOnlyFile_IsRefusedOnEverySystem()
    {
        var path = Path.Combine(_dir, "doc.adoc");
        await File.WriteAllTextAsync(path, "original");
        if (OperatingSystem.IsWindows())
            File.SetAttributes(path, FileAttributes.ReadOnly);
        else
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        try
        {
            // Linux and macOS would let the rename replace it.
            await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => TextFile.WriteAsync(path, "new", TextFile.Utf8NoBom));

            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(_dir));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Utf8WithBom_KeepsBom()
    {
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("= Café\n")).ToArray();

        Assert.Equal(original, await RoundTripAsync(original));
    }

    [Fact]
    public async Task Utf8WithoutBom_StaysWithoutBom()
    {
        var original = Encoding.UTF8.GetBytes("= Café\n");

        Assert.Equal(original, await RoundTripAsync(original));
    }

    [Fact]
    public async Task Windows1252_IsReadAsAnsiAndWrittenBackByteForByte()
    {
        // Every byte from space to 0xFF: invalid UTF-8, which used to become U+FFFD.
        var original = Enumerable.Range(0x20, 0xE0).Select(b => (byte)b).Append((byte)'\n').ToArray();

        var read = TextFile.Decode(original);

        Assert.DoesNotContain('�', read.Text);
        Assert.Contains('é', read.Text);
        Assert.Equal(original, await RoundTripAsync(original));
    }

    [Fact]
    public async Task BomButInvalidUtf8_IsWrittenBackByteForByte()
    {
        var original = Encoding.UTF8.GetPreamble().Concat(new byte[] { (byte)'a', 0xE9, (byte)'\n' }).ToArray();

        Assert.Equal(original, await RoundTripAsync(original));
    }

    [Fact]
    public void EncodingFor_LegacyEncodingThatCannotHoldText_FallsBackToUtf8()
    {
        Assert.Same(TextFile.Utf8NoBom, TextFile.EncodingFor("arrow →", TextFile.Ansi));
        Assert.Same(TextFile.Ansi, TextFile.EncodingFor("café", TextFile.Ansi));
    }

    [Fact]
    public async Task WriteAsync_ReplacesFileAndLeavesNoTempFile()
    {
        var path = Path.Combine(_dir, "doc.adoc");
        await File.WriteAllTextAsync(path, "old");

        await TextFile.WriteAsync(path, "new", TextFile.Utf8NoBom);

        Assert.Equal("new", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task WriteAsync_Failure_LeavesOriginalIntactAndNoTempFile()
    {
        if (!OperatingSystem.IsWindows())
            return; // Unix locks are advisory: the save goes through there
        var path = Path.Combine(_dir, "doc.adoc");
        await File.WriteAllTextAsync(path, "original");

        // A file locked by another program can't be replaced.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(() => TextFile.WriteAsync(path, "new", TextFile.Utf8NoBom));

        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_dir));
    }
}
