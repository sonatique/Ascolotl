namespace Ascolotl.Core.Models;

/// <summary>
/// Represents a heading in the document outline.
/// </summary>
public sealed record OutlineItem(string Title, int Level, int LineNumber);
