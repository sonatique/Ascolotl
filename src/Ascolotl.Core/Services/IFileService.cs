using System.Text;

namespace Ascolotl.Core.Services;

public interface IFileService
{
    /// <summary>Null when cancelled; Content is null when the chosen file can't be read.</summary>
    Task<(string Path, FileText? Content)?> OpenFileAsync();

    /// <summary>Null when the file is missing, locked or unreadable.</summary>
    Task<FileText?> ReadFileAsync(string path);

    /// <summary>Picks a path and writes there; null when cancelled. Throws when the write fails.</summary>
    Task<string?> SaveFileAsAsync(string suggestedName, string content, Encoding encoding);

    /// <summary>Writes atomically; throws when the write fails.</summary>
    Task SaveFileAsync(string path, string content, Encoding encoding);
    Task<string?> PickSavePathAsync(string suggestedName, string extension, string filterName);

    /// <summary>Folder picker for opening a project workspace. Null when cancelled.</summary>
    Task<string?> PickFolderAsync();

    /// <summary>
    /// Open picker for a non-AsciiDoc file (importers). Returns the chosen
    /// path, or null when cancelled.
    /// </summary>
    Task<string?> PickOpenFilePathAsync(string title, string filterName, IReadOnlyList<string> patterns);
}
