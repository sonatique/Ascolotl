using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IRecoveryStore
{
    void SaveSnapshot(string documentId, string sourceText, string? filePath, string displayName);
    string? LoadSnapshot(string documentId);
    void RemoveSnapshot(string documentId);
    List<RecoveryEntry> GetRecoveryEntries();
    void ClearAll();
}
