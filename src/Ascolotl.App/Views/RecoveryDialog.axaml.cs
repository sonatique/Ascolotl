using Avalonia.Controls;
using Ascolotl.Core.Resources;

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
        MessageText.Text = documentCount == 1
            ? Strings.RecoveryFoundOne
            : string.Format(Strings.RecoveryFoundMany, documentCount);
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
