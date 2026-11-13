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

    public Task<(string Path, string Content)?> OpenFileAsync()
    {
        return Task.FromResult(OpenFileResult);
    }

    public Task<string?> ReadFileAsync(string path)
    {
        ReadFileCalled++;
        return Task.FromResult(ReadFileResult);
    }

    public Task<string?> SaveFileAsAsync(string suggestedName, string content)
    {
        SaveFileAsCalled++;
        LastSavedContent = content;
        return Task.FromResult(SaveFileAsResult);
    }

    public Task SaveFileAsync(string path, string content)
    {
        SaveFileCalled++;
        LastSavedPath = path;
        LastSavedContent = content;
        return Task.CompletedTask;
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
