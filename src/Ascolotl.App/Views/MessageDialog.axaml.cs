using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ascolotl.App.Views;

/// <summary>A message the user acknowledges with OK (e.g. an export failure).</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message) : this()
    {
        Title = $"Ascolotl — {title}";
        MessageText.Text = message;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
