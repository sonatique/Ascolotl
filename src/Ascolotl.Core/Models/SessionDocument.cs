namespace Ascolotl.Core.Models;

public sealed class SessionDocument
{
    public required string DocumentId { get; set; }
    public string? FilePath { get; set; }
    public required string DisplayName { get; set; }
}
