using System.Text;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockFileService : IFileService
{
    public (string Path, string Content)? OpenFileResult { get; set; }
    public string? ReadFileResult { get; set; }
    public string? SaveFileAsResult { get; set; }
    public string? PickSavePathResult { get; set; }

    public int ReadFileCalled { get; private set; }
    public int SaveFileCalled { get; private set; }
    public int SaveFileAsCalled { get; private set; }
    public int PickSavePathCalled { get; private set; }

    public string? LastSavedPath { get; private set; }
    public string? LastSavedContent { get; private set; }
    public Encoding? LastSavedEncoding { get; private set; }

    /// <summary>Encoding reported with <see cref="ReadFileResult"/> and <see cref="OpenFileResult"/>.</summary>
    public Encoding ReadFileEncoding { get; set; } = TextFile.Utf8NoBom;

    /// <summary>Thrown by both save methods (e.g. a locked or read-only file).</summary>
    public Exception? SaveException { get; set; }

    /// <summary>Runs while a save is in progress (e.g. the user typing during the write).</summary>
    public Action? DuringSave { get; set; }

    public Task<(string Path, FileText? Content)?> OpenFileAsync()
    {
        return Task.FromResult<(string, FileText?)?>(
            OpenFileResult is { } r ? (r.Path, new FileText(r.Content, ReadFileEncoding)) : null);
    }

    public Task<FileText?> ReadFileAsync(string path)
    {
        ReadFileCalled++;
        return Task.FromResult(ReadFileResult is null ? null : new FileText(ReadFileResult, ReadFileEncoding));
    }

    public Task<string?> SaveFileAsAsync(string suggestedName, string content, Encoding encoding)
    {
        SaveFileAsCalled++;
        if (SaveFileAsResult is not null)
            Write(content, encoding);
        return Task.FromResult(SaveFileAsResult);
    }

    public Task SaveFileAsync(string path, string content, Encoding encoding)
    {
        SaveFileCalled++;
        LastSavedPath = path;
        Write(content, encoding);
        return Task.CompletedTask;
    }

    private void Write(string content, Encoding encoding)
    {
        DuringSave?.Invoke();
        if (SaveException is not null)
            throw SaveException;
        LastSavedContent = content;
        LastSavedEncoding = encoding;
    }

    public Task<string?> PickSavePathAsync(string suggestedName, string extension, string filterName)
    {
        PickSavePathCalled++;
        return Task.FromResult(PickSavePathResult);
    }

    public string? PickOpenFilePathResult { get; set; }
    public int PickOpenFilePathCalled { get; private set; }

    public Task<string?> PickOpenFilePathAsync(
        string title, string filterName, IReadOnlyList<string> patterns)
    {
        PickOpenFilePathCalled++;
        return Task.FromResult(PickOpenFilePathResult);
    }

    public string? PickFolderResult { get; set; }
    public int PickFolderCalled { get; private set; }

    public Task<string?> PickFolderAsync()
    {
        PickFolderCalled++;
        return Task.FromResult(PickFolderResult);
    }
}
