using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockDialogService : IDialogService
{
    public SaveConfirmationResult NextResult { get; set; } = SaveConfirmationResult.Cancel;
    public int ShowSaveConfirmationCalled { get; private set; }

    public FileConflictResult NextFileConflictResult { get; set; } = FileConflictResult.Cancel;
    public int ShowFileConflictCalled { get; private set; }

    public bool NextRecoveryResult { get; set; }
    public int ShowRecoveryDialogCalled { get; private set; }

    public Task<SaveConfirmationResult> ShowSaveConfirmationAsync(string documentName)
    {
        ShowSaveConfirmationCalled++;
        return Task.FromResult(NextResult);
    }

    public Task<FileConflictResult> ShowFileConflictAsync(string documentName)
    {
        ShowFileConflictCalled++;
        return Task.FromResult(NextFileConflictResult);
    }

    /// <summary>What the PDF title box returns; null = Cancel. Default: accept as shown.</summary>
    public Func<string, string?> PdfTitleAnswer { get; set; } = title => title;
    public string? LastPdfTitleShown { get; private set; }
    public int AskPdfTitleCalled { get; private set; }

    public Task<string?> AskPdfTitleAsync(string title)
    {
        AskPdfTitleCalled++;
        LastPdfTitleShown = title;
        return Task.FromResult(PdfTitleAnswer(title));
    }

    public Task<bool> ShowRecoveryDialogAsync(int documentCount)
    {
        ShowRecoveryDialogCalled++;
        return Task.FromResult(NextRecoveryResult);
    }
}
