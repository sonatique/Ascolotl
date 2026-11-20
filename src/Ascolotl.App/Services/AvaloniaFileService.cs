using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.Resources;

namespace Ascolotl.App.Services;

public sealed class AvaloniaFileService : IFileService
{
    private static readonly FilePickerFileType AdocFileType = new(Strings.FileTypeAsciiDoc)
    {
        Patterns = ["*.adoc", "*.asciidoc"],
    };

    private static readonly FilePickerFileType MarkdownFileType = new(Strings.FileTypeMarkdown)
    {
        Patterns = [.. MarkdownFile.Extensions.Select(e => "*" + e)],
    };

    private static readonly FilePickerFileType DocumentFileType = new(Strings.FileTypeDocuments)
    {
        Patterns = [.. AdocFileType.Patterns!, .. MarkdownFileType.Patterns!],
    };

    private static readonly FilePickerFileType TextFileType = new(Strings.FileTypeText)
    {
        Patterns = ["*.txt"],
    };

    private readonly Window _window;

    public AvaloniaFileService(Window window)
    {
        _window = window;
    }

    public async Task<(string Path, FileText? Content)?> OpenFileAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.DialogOpenDocument,
            AllowMultiple = false,
            FileTypeFilter = [DocumentFileType, TextFileType, FilePickerFileTypes.All],
        });

        if (files.Count == 0)
            return null;

        var file = files[0];
        var localPath = file.TryGetLocalPath();
        if (localPath is null)
            return null;

        return (localPath, await ReadFileAsync(localPath));
    }

    public async Task<FileText?> ReadFileAsync(string path)
    {
        try
        {
            return await TextFile.ReadAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, locked by another program, or no permission: callers report it.
            return null;
        }
    }

    public async Task<string?> SaveFileAsAsync(string suggestedName, string content, Encoding encoding)
    {
        // A Markdown document offers Markdown first, so it stays Markdown.
        var markdown = MarkdownFile.Is(suggestedName);
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.DialogSaveDocument,
            SuggestedFileName = suggestedName,
            DefaultExtension = markdown ? "md" : "adoc",
            FileTypeChoices = markdown
                ? [MarkdownFileType, AdocFileType, TextFileType, FilePickerFileTypes.All]
                : [AdocFileType, MarkdownFileType, TextFileType, FilePickerFileTypes.All],
        });

        if (file is null)
            return null;

        var localPath = file.TryGetLocalPath();
        if (localPath is null)
            return null;

        await TextFile.WriteAsync(localPath, content, encoding);

        return localPath;
    }

    public Task SaveFileAsync(string path, string content, Encoding encoding)
        => TextFile.WriteAsync(path, content, encoding);

    public async Task<string?> PickSavePathAsync(string suggestedName, string extension, string filterName)
    {
        var fileType = new FilePickerFileType(filterName)
        {
            Patterns = [$"*.{extension}"],
        };

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = string.Format(Strings.DialogExportAs, filterName),
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
            Title = Strings.DialogOpenFolder,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
