namespace Ascolotl.Core.Services;

public interface IFileService
{
    Task<(string Path, string Content)?> OpenFileAsync();
    Task<string?> ReadFileAsync(string path);
    Task<string?> SaveFileAsAsync(string suggestedName, string content);
    Task SaveFileAsync(string path, string content);
    Task<string?> PickSavePathAsync(string suggestedName, string extension, string filterName);

    /// <summary>Folder picker for opening a project workspace. Null when cancelled.</summary>
    Task<string?> PickFolderAsync();

    /// <summary>
    /// Open picker for a non-AsciiDoc file (importers). Returns the chosen
    /// path, or null when cancelled.
    /// </summary>
    Task<string?> PickOpenFilePathAsync(string title, string filterName, IReadOnlyList<string> patterns);
}
