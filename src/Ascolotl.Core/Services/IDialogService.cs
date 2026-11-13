using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface IDialogService
{
    Task<SaveConfirmationResult> ShowSaveConfirmationAsync(string documentName);
    Task<FileConflictResult> ShowFileConflictAsync(string documentName);
    Task<bool> ShowRecoveryDialogAsync(int documentCount);
    /// <summary>Lets the user edit a PDF export's title; null when cancelled.</summary>
    Task<string?> AskPdfTitleAsync(string title);
    /// <summary>Tells the user something failed; returns when they dismiss it.</summary>
    Task ShowErrorAsync(string title, string message);
}
