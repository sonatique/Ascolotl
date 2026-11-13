namespace Ascolotl.Core.Models;

public sealed class RecoveryEntry
{
    public required string DocumentId { get; set; }
    public string? OriginalFilePath { get; set; }
    public required string DisplayName { get; set; }
    public DateTime Timestamp { get; set; }
}
