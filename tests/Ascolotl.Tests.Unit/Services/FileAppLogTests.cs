using Ascolotl.Infrastructure.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class FileAppLogTests : IDisposable
{
    private readonly string _logDir;

    public FileAppLogTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), $"adocforge-log-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_logDir, recursive: true); }
        catch { /* cleanup */ }
    }

    private static string ReadLogFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Info_WritesToLogFile()
    {
        using var log = new FileAppLog(_logDir);
        log.Info("test message");

        var files = Directory.GetFiles(_logDir, "adocforge-*.log");
        Assert.Single(files);

        var content = ReadLogFile(files[0]);
        Assert.Contains("[INFO] test message", content);
    }

    [Fact]
    public void Error_IncludesExceptionInfo()
    {
        using var log = new FileAppLog(_logDir);
        log.Error("something broke", new InvalidOperationException("bad state"));

        var files = Directory.GetFiles(_logDir, "adocforge-*.log");
        var content = ReadLogFile(files[0]);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("bad state", content);
    }

    [Fact]
    public void Warn_WritesWarnLevel()
    {
        using var log = new FileAppLog(_logDir);
        log.Warn("caution");

        var files = Directory.GetFiles(_logDir, "adocforge-*.log");
        var content = ReadLogFile(files[0]);
        Assert.Contains("[WARN] caution", content);
    }

    [Fact]
    public void CreatesLogDirectory()
    {
        var subDir = Path.Combine(_logDir, "nested");
        using var log = new FileAppLog(subDir);
        log.Info("test");

        Assert.True(Directory.Exists(subDir));
    }
}
