using Avalonia.Controls;
using Ascolotl.App.Views;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.App.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Window _owner;

    public AvaloniaDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<SaveConfirmationResult> ShowSaveConfirmationAsync(string documentName)
    {
        var dialog = new SaveConfirmationDialog(documentName);
        await dialog.ShowDialog(_owner);
        return dialog.Result;
    }

    public async Task<FileConflictResult> ShowFileConflictAsync(string documentName)
    {
        var dialog = new FileConflictDialog(documentName);
        await dialog.ShowDialog(_owner);
        return dialog.Result;
    }

    public async Task<bool> ShowRecoveryDialogAsync(int documentCount)
    {
        var dialog = new RecoveryDialog(documentCount);
        await dialog.ShowDialog(_owner);
        return dialog.ShouldRecover;
    }

    public async Task<string?> AskPdfTitleAsync(string title)
    {
        var dialog = new PdfTitleDialog(title);
        await dialog.ShowDialog(_owner);
        return dialog.Result;
    }
}
