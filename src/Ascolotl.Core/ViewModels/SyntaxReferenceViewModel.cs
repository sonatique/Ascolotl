using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Ascolotl.Core.Editing.Assist;

namespace Ascolotl.Core.ViewModels;

/// <summary>
/// The searchable AsciiDoc syntax reference (F1): filters the assist catalog
/// by name/trigger/description; the selected entry's snippet is inserted into
/// the editor by the view.
/// </summary>
public partial class SyntaxReferenceViewModel : ObservableObject
{
    private readonly IReadOnlyList<AssistItem> _all;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isOpen;

    public ObservableCollection<AssistItem> FilteredItems { get; } = [];

    public SyntaxReferenceViewModel(IReadOnlyList<AssistItem>? items = null)
    {
        _all = items ?? AsciiDocAssistCatalog.ReferenceItems;
        RefreshFilter();
    }

    public AssistItem? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < FilteredItems.Count
            ? FilteredItems[SelectedIndex]
            : null;

    public void Open()
    {
        FilterText = string.Empty;
        SelectedIndex = 0;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void MoveUp() => SelectedIndex = Math.Max(0, SelectedIndex - 1);

    public void MoveDown() =>
        SelectedIndex = Math.Min(FilteredItems.Count - 1, SelectedIndex + 1);

    partial void OnFilterTextChanged(string value) => RefreshFilter();

    private void RefreshFilter()
    {
        FilteredItems.Clear();
        foreach (var item in _all)
        {
            if (FilterText.Length == 0
                || item.Label.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                || item.Trigger.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
        SelectedIndex = FilteredItems.Count > 0 ? 0 : -1;
    }
}
