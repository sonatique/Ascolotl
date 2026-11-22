namespace Ascolotl.Core.Models;

/// <summary>
/// Represents a single entry in the command palette.
/// </summary>
public sealed record CommandEntry(
    string Name,
    string? Shortcut,
    string Category,
    Action Execute,
    Func<bool>? CanExecute = null)
{
    /// <summary>What a screen reader announces for the palette row.</summary>
    public override string ToString() => Shortcut is null ? Name : $"{Name}, {Shortcut}";
}
