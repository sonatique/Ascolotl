using Avalonia.Controls;
using Ascolotl.Core.Models;

namespace Ascolotl.App.Views;

public partial class SaveConfirmationDialog : Window
{
    public SaveConfirmationResult Result { get; private set; } = SaveConfirmationResult.Cancel;

    public SaveConfirmationDialog()
    {
        InitializeComponent();
    }

    public SaveConfirmationDialog(string documentName) : this()
    {
        MessageText.Text = $"Do you want to save changes to \"{documentName}\"?";
    }

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = SaveConfirmationResult.Save;
        Close();
    }

    private void OnDontSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = SaveConfirmationResult.DontSave;
        Close();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Result = SaveConfirmationResult.Cancel;
        Close();
    }
}
