using Ascolotl.Core.Models;

namespace Ascolotl.Core.Editing;

/// <summary>
/// Pure construction of the project sidebar's file tree from a flat list of
/// relative paths — the caller enumerates the filesystem, these helpers decide
/// what to show and how to nest/sort it, so the logic is unit-testable.
/// </summary>
public static class ProjectTreeBuilder
{
    private static readonly string[] DocumentExtensions = [".adoc", ".asciidoc"];

    /// <summary>
    /// Keep only AsciiDoc documents, and drop anything under a dot-directory
    /// (.git, .vs, …) — those are never part of the writing project.
    /// </summary>
    public static IEnumerable<string> FilterDocumentPaths(IEnumerable<string> relativePaths)
    {
        foreach (var rel in relativePaths)
        {
            var parts = rel.Split('/', '\\');
            if (parts.Any(p => p.StartsWith('.'))) continue;
            var ext = Path.GetExtension(rel);
            if (DocumentExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                yield return rel;
        }
    }

    /// <summary>
    /// Nest relative paths into a tree rooted at <paramref name="rootPath"/>:
    /// folders first, then files, both alphabetical (case-insensitive). Folders
    /// only appear when they (transitively) contain a document.
    /// </summary>
    public static IReadOnlyList<ProjectNode> Build(string rootPath, IEnumerable<string> relativeFilePaths)
    {
        var roots = new List<ProjectNode>();

        foreach (var rel in relativeFilePaths)
        {
            var parts = rel.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var children = roots;
            var current = rootPath;
            for (var i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);
                var isDir = i < parts.Length - 1;

                var existing = children.FirstOrDefault(n =>
                    n.IsDirectory == isDir
                    && string.Equals(n.Name, parts[i], StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    existing = new ProjectNode { Name = parts[i], FullPath = current, IsDirectory = isDir };
                    children.Add(existing);
                }
                children = existing.Children;
            }
        }

        SortRecursively(roots);
        return roots;
    }

    private static void SortRecursively(List<ProjectNode> nodes)
    {
        nodes.Sort((a, b) => a.IsDirectory != b.IsDirectory
            ? (a.IsDirectory ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var n in nodes)
            SortRecursively(n.Children);
    }
}
