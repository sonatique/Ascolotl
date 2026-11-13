using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ascolotl.Core.Models;

namespace Ascolotl.Core.ViewModels;

/// <summary>
/// ViewModel for the command palette overlay (Ctrl+Shift+P).
/// Filters, navigates, and executes commands.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isOpen;

    public ObservableCollection<CommandEntry> FilteredCommands { get; } = [];

    public IReadOnlyList<CommandEntry> AllCommands { get; }

    public CommandPaletteViewModel(IReadOnlyList<CommandEntry> commands)
    {
        AllCommands = commands;
        RefreshFilter();
    }

    public void Open()
    {
        FilterText = string.Empty;
        SelectedIndex = 0;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }

    public void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < FilteredCommands.Count)
        {
            var command = FilteredCommands[SelectedIndex];
            Close();
            command.Execute();
        }
    }

    public void MoveUp()
    {
        if (SelectedIndex > 0)
            SelectedIndex--;
    }

    public void MoveDown()
    {
        if (SelectedIndex < FilteredCommands.Count - 1)
            SelectedIndex++;
    }

    partial void OnFilterTextChanged(string value)
    {
        RefreshFilter();
        SelectedIndex = 0;
    }

    private void RefreshFilter()
    {
        FilteredCommands.Clear();

        foreach (var cmd in AllCommands)
        {
            if (string.IsNullOrEmpty(FilterText)
                || cmd.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                if (cmd.CanExecute is null || cmd.CanExecute())
                    FilteredCommands.Add(cmd);
            }
        }
    }
}
