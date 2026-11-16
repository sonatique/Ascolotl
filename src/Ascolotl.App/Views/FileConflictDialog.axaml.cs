using Avalonia.Controls;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;

namespace Ascolotl.App.Views;

public partial class FileConflictDialog : Window
{
    public FileConflictResult Result { get; private set; } = FileConflictResult.Cancel;

    public FileConflictDialog()
    {
        InitializeComponent();
    }

    public FileConflictDialog(string documentName) : this()
    {
        MessageText.Text = string.Format(Strings.FileConflictMessage, documentName);
    }

    private void OnOverwriteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = FileConflictResult.Overwrite;
        Close();
    }

    private void OnReloadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = FileConflictResult.Reload;
        Close();
    }

    private void OnSaveAsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = FileConflictResult.SaveAs;
        Close();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = FileConflictResult.Cancel;
        Close();
    }
}
