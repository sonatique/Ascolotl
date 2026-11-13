using Ascolotl.Core.Models;
using Ascolotl.Infrastructure.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _testPath;
    private readonly SessionStore _store;

    public SessionStoreTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"adocforge-session-test-{Guid.NewGuid():N}.json");
        _store = new SessionStore(_testPath);
    }

    public void Dispose()
    {
        try { File.Delete(_testPath); }
        catch { /* cleanup */ }
    }

    [Fact]
    public void Load_ReturnsNull_WhenNoFile()
    {
        Assert.Null(_store.Load());
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var state = new SessionState
        {
            SelectedIndex = 1,
            OpenDocuments =
            [
                new SessionDocument { DocumentId = "abc", FilePath = "/test.adoc", DisplayName = "test.adoc" },
                new SessionDocument { DocumentId = "def", FilePath = "/other.adoc", DisplayName = "other.adoc" },
            ]
        };

        _store.Save(state);
        var loaded = _store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.SelectedIndex);
        Assert.Equal(2, loaded.OpenDocuments.Count);
        Assert.Equal("abc", loaded.OpenDocuments[0].DocumentId);
        Assert.Equal("/test.adoc", loaded.OpenDocuments[0].FilePath);
    }

    [Fact]
    public void Save_OverwritesPreviousSession()
    {
        var state1 = new SessionState { SelectedIndex = 0 };
        var state2 = new SessionState { SelectedIndex = 5 };

        _store.Save(state1);
        _store.Save(state2);

        var loaded = _store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.SelectedIndex);
    }

    [Fact]
    public void Load_ReturnsNull_WhenCorruptFile()
    {
        File.WriteAllText(_testPath, "not valid json {{{");
        Assert.Null(_store.Load());
    }
}
