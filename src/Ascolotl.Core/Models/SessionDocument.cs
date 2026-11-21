namespace Ascolotl.Core.Models;

public sealed class SessionDocument
{
    public required string DocumentId { get; set; }
    public string? FilePath { get; set; }
    public required string DisplayName { get; set; }
    /// <summary>0-based source line at the top of the view.</summary>
    public int TopLine { get; set; }
    public EditorMode EditorMode { get; set; } = EditorMode.Wysiwyg;
    /// <summary>Page Preview position, 0 to 1 of the pages' height.</summary>
    public double PageScroll { get; set; }
}
