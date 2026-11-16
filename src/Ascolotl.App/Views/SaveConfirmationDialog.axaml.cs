using Avalonia.Controls;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;

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
        MessageText.Text = string.Format(Strings.SaveConfirmationMessage, documentName);
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
