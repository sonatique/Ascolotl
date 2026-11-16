using Ascolotl.Infrastructure.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class FileAppLogTests : IDisposable
{
    private readonly string _logDir;

    public FileAppLogTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), $"ascolotl-log-test-{Guid.NewGuid():N}");
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

        var files = Directory.GetFiles(_logDir, "ascolotl-*.log");
        Assert.Single(files);

        var content = ReadLogFile(files[0]);
        Assert.Contains("[INFO] test message", content);
    }

    [Fact]
    public void Error_IncludesExceptionInfo()
    {
        using var log = new FileAppLog(_logDir);
        log.Error("something broke", new InvalidOperationException("bad state"));

        var files = Directory.GetFiles(_logDir, "ascolotl-*.log");
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

        var files = Directory.GetFiles(_logDir, "ascolotl-*.log");
        var content = ReadLogFile(files[0]);
        Assert.Contains("[WARN] caution", content);
    }

    [Fact]
    public void Startup_DeletesWeekOldLogs_IncludingThoseUnderTheOldName()
    {
        Directory.CreateDirectory(_logDir);
        string Log(string name, int daysOld)
        {
            var path = Path.Combine(_logDir, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTime(path, DateTime.Now.AddDays(-daysOld));
            return path;
        }
        var oldCurrent = Log("ascolotl-2026-01-01.log", 30);
        var oldLegacy = Log("adocforge-2026-01-01.log", 30);
        var recentLegacy = Log("adocforge-2026-11-14.log", 1);

        using var log = new FileAppLog(_logDir); // cleans up in the background

        Assert.True(SpinWait.SpinUntil(() => !File.Exists(oldCurrent) && !File.Exists(oldLegacy), TimeSpan.FromSeconds(10)));
        Assert.True(File.Exists(recentLegacy));
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
