using Ascolotl.Core.Models;
using Avalonia.Controls;
using Avalonia.Input;

namespace Ascolotl.App.Views;

/// <summary>Every key and what it does, in a window kept open beside the document. Esc closes it.</summary>
public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow() => InitializeComponent();

    public KeyboardShortcutsWindow(IReadOnlyList<ShortcutGroup> groups) : this()
    {
        ShortcutGroups.ItemsSource = groups;
        Opened += (_, _) => ShortcutsScroll.Focus(); // the arrows and Page Up/Down scroll
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
