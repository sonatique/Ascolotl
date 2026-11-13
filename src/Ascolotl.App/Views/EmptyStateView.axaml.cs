using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ascolotl.App.Views;

public partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
