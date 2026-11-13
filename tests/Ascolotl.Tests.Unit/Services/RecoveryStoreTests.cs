using Ascolotl.Infrastructure.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class RecoveryStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly RecoveryStore _store;

    public RecoveryStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"adocforge-recovery-test-{Guid.NewGuid():N}");
        _store = new RecoveryStore(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* cleanup */ }
    }

    [Fact]
    public void GetRecoveryEntries_EmptyByDefault()
    {
        var entries = _store.GetRecoveryEntries();
        Assert.Empty(entries);
    }

    [Fact]
    public void SaveSnapshot_CreatesSnapshotFile()
    {
        _store.SaveSnapshot("doc1", "= Hello", "/tmp/test.adoc", "test.adoc");

        var snapshot = _store.LoadSnapshot("doc1");
        Assert.Equal("= Hello", snapshot);
    }

    [Fact]
    public void SaveSnapshot_AddsManifestEntry()
    {
        _store.SaveSnapshot("doc1", "content", "/tmp/test.adoc", "test.adoc");

        var entries = _store.GetRecoveryEntries();
        Assert.Single(entries);
        Assert.Equal("doc1", entries[0].DocumentId);
        Assert.Equal("/tmp/test.adoc", entries[0].OriginalFilePath);
        Assert.Equal("test.adoc", entries[0].DisplayName);
    }

    [Fact]
    public void SaveSnapshot_UpdatesExistingEntry()
    {
        _store.SaveSnapshot("doc1", "v1", null, "Untitled 1");
        _store.SaveSnapshot("doc1", "v2", null, "Untitled 1");

        var entries = _store.GetRecoveryEntries();
        Assert.Single(entries);
        Assert.Equal("v2", _store.LoadSnapshot("doc1"));
    }

    [Fact]
    public void RemoveSnapshot_DeletesSnapshotAndEntry()
    {
        _store.SaveSnapshot("doc1", "content", null, "test");
        _store.RemoveSnapshot("doc1");

        Assert.Null(_store.LoadSnapshot("doc1"));
        Assert.Empty(_store.GetRecoveryEntries());
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        _store.SaveSnapshot("doc1", "a", null, "A");
        _store.SaveSnapshot("doc2", "b", null, "B");
        _store.ClearAll();

        Assert.Empty(_store.GetRecoveryEntries());
    }

    [Fact]
    public void LoadSnapshot_ReturnsNull_WhenNotFound()
    {
        Assert.Null(_store.LoadSnapshot("nonexistent"));
    }

    [Fact]
    public void SaveSnapshot_HandlesUntitledDocument()
    {
        _store.SaveSnapshot("doc1", "untitled content", null, "Untitled 1");

        var entries = _store.GetRecoveryEntries();
        Assert.Single(entries);
        Assert.Null(entries[0].OriginalFilePath);
        Assert.Equal("Untitled 1", entries[0].DisplayName);
    }

    [Fact]
    public void MultipleDocuments_MaintainedSeparately()
    {
        _store.SaveSnapshot("doc1", "content1", "/a.adoc", "a.adoc");
        _store.SaveSnapshot("doc2", "content2", "/b.adoc", "b.adoc");

        Assert.Equal(2, _store.GetRecoveryEntries().Count);
        Assert.Equal("content1", _store.LoadSnapshot("doc1"));
        Assert.Equal("content2", _store.LoadSnapshot("doc2"));
    }
}
