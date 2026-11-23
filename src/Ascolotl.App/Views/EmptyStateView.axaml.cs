using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Ascolotl.App.Views;

public partial class EmptyStateView : UserControl
{
    /// <summary>The key to open a file, written as the menus write keys ("Ctrl+O", on a Mac with Cmd).</summary>
    public static string OpenKey => CommandKey.OnPlatform(new KeyGesture(Key.O, KeyModifiers.Control)).ToString("p", null);

    public EmptyStateView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
