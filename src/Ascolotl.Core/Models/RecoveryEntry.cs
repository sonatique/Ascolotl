namespace Ascolotl.Core.Models;

public sealed class RecoveryEntry
{
    public required string DocumentId { get; set; }
    public string? OriginalFilePath { get; set; }
    public required string DisplayName { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>Last write time (UTC) of the file the edits were based on; null in older manifests.</summary>
    public DateTime? FileWriteTime { get; set; }
}
