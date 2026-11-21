namespace Ascolotl.Core.Models;

public sealed class SessionState
{
    public List<SessionDocument> OpenDocuments { get; set; } = [];
    /// <summary>Index into <see cref="OpenDocuments"/> of the tab in front.</summary>
    public int SelectedIndex { get; set; }
}
