namespace Ascolotl.Core.Models;

/// <summary>
/// Represents a single entry in the command palette.
/// </summary>
public sealed record CommandEntry(
    string Name,
    string? Shortcut,
    string Category,
    Action Execute,
    Func<bool>? CanExecute = null);
