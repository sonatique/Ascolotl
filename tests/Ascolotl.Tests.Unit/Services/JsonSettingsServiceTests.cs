using Ascolotl.Core.Models;
using Ascolotl.Infrastructure.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Ascolotl_Test_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private JsonSettingsService CreateService()
        => new(Path.Combine(_tempDir, "settings.json"));

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var service = CreateService();

        var settings = service.Load();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Empty(settings.RecentFiles);
        Assert.Equal(1280, settings.WindowWidth);
        Assert.Equal(800, settings.WindowHeight);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var service = CreateService();
        var original = new AppSettings
        {
            Theme = AppTheme.Dark,
            EditorMode = EditorMode.Source,
            ShowTitleAndToc = true,
            SyncScrolling = false,
            WindowWidth = 1920,
            WindowHeight = 1080,
            WindowMaximized = true,
            RecentFiles = ["file1.adoc", "file2.adoc"],
        };

        service.Save(original);
        var loaded = service.Load();

        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(EditorMode.Source, loaded.EditorMode);
        Assert.True(loaded.ShowTitleAndToc);
        Assert.False(loaded.SyncScrolling);
        Assert.Equal(1920, loaded.WindowWidth);
        Assert.Equal(1080, loaded.WindowHeight);
        Assert.True(loaded.WindowMaximized);
        Assert.Equal(["file1.adoc", "file2.adoc"], loaded.RecentFiles);
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_ReturnsDefaults()
    {
        var service = CreateService();
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "not valid json {{{");

        var settings = service.Load();

        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var service = CreateService();
        Assert.False(Directory.Exists(_tempDir));

        service.Save(new AppSettings());

        Assert.True(File.Exists(Path.Combine(_tempDir, "settings.json")));
    }

    [Fact]
    public void Settings_DefaultValues_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(EditorMode.Split, settings.EditorMode);
        Assert.False(settings.ShowTitleAndToc);
        Assert.True(settings.SyncScrolling);
        Assert.Equal(1280, settings.WindowWidth);
        Assert.Equal(800, settings.WindowHeight);
        Assert.Equal(100, settings.WindowX);
        Assert.Equal(100, settings.WindowY);
        Assert.False(settings.WindowMaximized);
        Assert.Empty(settings.RecentFiles);
    }
}
