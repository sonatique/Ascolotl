using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ascolotl.Core.Services;

namespace Ascolotl.App.Services;

public sealed class AvaloniaFileService : IFileService
{
    private static readonly FilePickerFileType AdocFileType = new("AsciiDoc Files")
    {
        Patterns = ["*.adoc", "*.asciidoc"],
    };

    private static readonly FilePickerFileType TextFileType = new("Text Files")
    {
        Patterns = ["*.txt"],
    };

    private readonly Window _window;

    public AvaloniaFileService(Window window)
    {
        _window = window;
    }

    public async Task<(string Path, string Content)?> OpenFileAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AsciiDoc File",
            AllowMultiple = false,
            FileTypeFilter = [AdocFileType, TextFileType, FilePickerFileTypes.All],
        });

        if (files.Count == 0)
            return null;

        var file = files[0];
        var localPath = file.TryGetLocalPath();
        if (localPath is null)
            return null;

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        return (localPath, content);
    }

    public async Task<string?> ReadFileAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<string?> SaveFileAsAsync(string suggestedName, string content)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save AsciiDoc File",
            SuggestedFileName = suggestedName,
            DefaultExtension = "adoc",
            FileTypeChoices = [AdocFileType, TextFileType, FilePickerFileTypes.All],
        });

        if (file is null)
            return null;

        var localPath = file.TryGetLocalPath();
        if (localPath is null)
            return null;

        await File.WriteAllTextAsync(localPath, content);

        return localPath;
    }

    public async Task SaveFileAsync(string path, string content)
    {
        await File.WriteAllTextAsync(path, content);
    }

    public async Task<string?> PickSavePathAsync(string suggestedName, string extension, string filterName)
    {
        var fileType = new FilePickerFileType(filterName)
        {
            Patterns = [$"*.{extension}"],
        };

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {filterName}",
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [fileType],
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFilePathAsync(
        string title, string filterName, IReadOnlyList<string> patterns)
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(filterName) { Patterns = [.. patterns] },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
