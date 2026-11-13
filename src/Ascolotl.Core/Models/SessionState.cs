namespace Ascolotl.Core.Models;

public sealed class SessionState
{
    public List<SessionDocument> OpenDocuments { get; set; } = [];
    public int SelectedIndex { get; set; }
}
