using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ascolotl.App.Views;

/// <summary>
/// The application menus, built once per menu layout (hamburger and menu bar)
/// so switching layouts only changes which set is visible.
/// </summary>
public partial class AppMenus : MenuItem
{
    // Styled exactly like a MenuItem (Fluent picks control themes by type).
    protected override Type StyleKeyOverride => typeof(MenuItem);

    public AppMenus()
    {
        InitializeComponent();
    }

    // The menu-bar set's items live in the window's Menu, not under this
    // object, so find the window from the clicked item.
    private void OnExitClick(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(sender as Visual) as Window)?.Close();
}
