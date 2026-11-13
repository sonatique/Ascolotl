using Avalonia.Controls;

namespace Ascolotl.App.Views;

public partial class RecoveryDialog : Window
{
    public bool ShouldRecover { get; private set; }

    public RecoveryDialog()
    {
        InitializeComponent();
    }

    public RecoveryDialog(int documentCount) : this()
    {
        var plural = documentCount == 1 ? "document" : "documents";
        MessageText.Text = $"Ascolotl found {documentCount} unsaved {plural} from a previous session.";
    }

    private void OnRecoverClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShouldRecover = true;
        Close();
    }

    private void OnDiscardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShouldRecover = false;
        Close();
    }
}
