using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockRecoveryStore : IRecoveryStore
{
    private readonly Dictionary<string, string> _snapshots = new();
    private List<RecoveryEntry> _entries = [];

    public int SaveSnapshotCalled { get; private set; }
    public int RemoveSnapshotCalled { get; private set; }
    public int ClearAllCalled { get; private set; }
    public string? LastSavedDocumentId { get; private set; }
    public string? LastSavedSourceText { get; private set; }

    public void SetEntries(List<RecoveryEntry> entries) => _entries = entries;
    public void SetSnapshot(string documentId, string sourceText) => _snapshots[documentId] = sourceText;

    public void SaveSnapshot(string documentId, string sourceText, string? filePath, string displayName)
    {
        SaveSnapshotCalled++;
        LastSavedDocumentId = documentId;
        LastSavedSourceText = sourceText;
        _snapshots[documentId] = sourceText;
    }

    public string? LoadSnapshot(string documentId)
        => _snapshots.TryGetValue(documentId, out var text) ? text : null;

    public void RemoveSnapshot(string documentId)
    {
        RemoveSnapshotCalled++;
        _snapshots.Remove(documentId);
    }

    public List<RecoveryEntry> GetRecoveryEntries() => _entries;

    public void ClearAll()
    {
        ClearAllCalled++;
        _entries.Clear();
        _snapshots.Clear();
    }
}
