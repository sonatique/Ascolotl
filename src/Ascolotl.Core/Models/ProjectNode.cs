namespace Ascolotl.Core.Models;

/// <summary>One entry in the project file tree — a folder or a document.</summary>
public sealed class ProjectNode
{
    public required string Name { get; init; }

    /// <summary>Absolute path of the file or folder.</summary>
    public required string FullPath { get; init; }

    public bool IsDirectory { get; init; }

    public List<ProjectNode> Children { get; } = [];
}
