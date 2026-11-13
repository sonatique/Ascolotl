using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ascolotl.App.Views;

public partial class PdfTitleDialog : Window
{
    /// <summary>The edited title; null when cancelled.</summary>
    public string? Result { get; private set; }

    public PdfTitleDialog()
    {
        InitializeComponent();
    }

    public PdfTitleDialog(string title) : this()
    {
        TitleBox.Text = title;
        Opened += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    private void OnExportClick(object? sender, RoutedEventArgs e)
    {
        Result = (TitleBox.Text ?? "").Trim();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
