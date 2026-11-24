using Ascolotl.Core.Editing.Assist;
using Ascolotl.Core.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace Ascolotl.App.Views;

/// <summary>The syntax reference (F1), in a window kept open beside the document: Enter or a
/// double-click inserts the entry's syntax in the source, Esc closes it.</summary>
public partial class SyntaxReferenceWindow : Window
{
    /// <summary>Asked to insert the entry: the main window puts it at the source's caret.</summary>
    public event Action<AssistItem>? Insert;

    public SyntaxReferenceWindow()
    {
        InitializeComponent();
        SyntaxReferenceList.DoubleTapped += (_, _) => InsertSelected();
        Opened += (_, _) => FocusSearch();
    }

    /// <summary>The search box takes the keyboard, its text selected to type over.</summary>
    public void FocusSearch()
    {
        SyntaxReferenceFilter.Focus();
        SyntaxReferenceFilter.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not SyntaxReferenceViewModel vm) return;
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Enter: InsertSelected(); break;
            case Key.Up when SyntaxReferenceFilter.IsFocused: vm.MoveUp(); break;
            case Key.Down when SyntaxReferenceFilter.IsFocused: vm.MoveDown(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void InsertSelected()
    {
        if ((DataContext as SyntaxReferenceViewModel)?.SelectedItem is { } item) Insert?.Invoke(item);
    }
}
