using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IRecoveryStore
{
    /// <param name="fileWriteTime">Write time of the file the edits are based on, for conflict detection after recovery.</param>
    void SaveSnapshot(string documentId, string sourceText, string? filePath, string displayName, DateTime? fileWriteTime = null);
    string? LoadSnapshot(string documentId);
    void RemoveSnapshot(string documentId);
    List<RecoveryEntry> GetRecoveryEntries();
    void ClearAll();
}
