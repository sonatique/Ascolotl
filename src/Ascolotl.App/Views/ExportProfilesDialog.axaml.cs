using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ascolotl.Core.Models;

namespace Ascolotl.App.Views;

/// <summary>
/// Compact editor for the export-profile list. Edits the collection in place;
/// the owner persists and rebuilds the Export menu after the dialog closes.
/// </summary>
public partial class ExportProfilesDialog : Window
{
    private static readonly ExportFormat[] Formats =
        [ExportFormat.Pdf, ExportFormat.PdfLandscape, ExportFormat.Html, ExportFormat.DocBook, ExportFormat.Epub];

    private static readonly ExportTheme[] Themes =
        [ExportTheme.Asciidoctor, ExportTheme.Clean, ExportTheme.Github];

    private readonly ObservableCollection<ExportProfile> _profiles;
    private ExportProfile? _current;
    private bool _loadingFields;

    public ExportProfilesDialog()
        : this([]) { }

    public ExportProfilesDialog(ObservableCollection<ExportProfile> profiles)
    {
        InitializeComponent();
        _profiles = profiles;

        FormatBox.ItemsSource = Formats.Select(f => f.DisplayName()).ToList();
        ThemeBox.ItemsSource = Themes.Select(t => t.DisplayName()).ToList();

        ProfileList.SelectionChanged += (_, _) => LoadSelected();
        AddButton.Click += OnAdd;
        RemoveButton.Click += OnRemove;
        BrowseButton.Click += OnBrowse;

        NameBox.TextChanged += (_, _) => ApplyFields();
        FolderBox.TextChanged += (_, _) => ApplyFields();
        FormatBox.SelectionChanged += (_, _) => ApplyFields();
        ThemeBox.SelectionChanged += (_, _) => ApplyFields();

        RefreshList(selectIndex: _profiles.Count > 0 ? 0 : -1);
    }

    private void RefreshList(int selectIndex)
    {
        ProfileList.ItemsSource = _profiles.Select(p => p.Name).ToList();
        ProfileList.SelectedIndex = Math.Min(selectIndex, _profiles.Count - 1);
        LoadSelected();
    }

    private void LoadSelected()
    {
        var i = ProfileList.SelectedIndex;
        _current = i >= 0 && i < _profiles.Count ? _profiles[i] : null;

        _loadingFields = true;
        try
        {
            var has = _current is not null;
            NameBox.IsEnabled = FormatBox.IsEnabled = ThemeBox.IsEnabled =
                FolderBox.IsEnabled = BrowseButton.IsEnabled = has;
            NoSelectionHint.IsVisible = !has;

            NameBox.Text = _current?.Name ?? string.Empty;
            FolderBox.Text = _current?.OutputFolder ?? string.Empty;
            FormatBox.SelectedIndex = _current is null ? -1 : Array.IndexOf(Formats, _current.Format);
            ThemeBox.SelectedIndex = _current is null ? -1 : Array.IndexOf(Themes, _current.Theme);
        }
        finally
        {
            _loadingFields = false;
        }
    }

    private void ApplyFields()
    {
        if (_loadingFields || _current is null) return;

        _current.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Unnamed" : NameBox.Text!.Trim();
        _current.OutputFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text!.Trim();
        if (FormatBox.SelectedIndex >= 0 && FormatBox.SelectedIndex < Formats.Length)
            _current.Format = Formats[FormatBox.SelectedIndex];
        if (ThemeBox.SelectedIndex >= 0 && ThemeBox.SelectedIndex < Themes.Length)
            _current.Theme = Themes[ThemeBox.SelectedIndex];

        // Reflect the (possibly renamed) profile in the list without resetting
        // the selection or focus.
        var i = ProfileList.SelectedIndex;
        if (ProfileList.ItemsSource is List<string> names && i >= 0 && i < names.Count
            && names[i] != _current.Name)
        {
            names[i] = _current.Name;
            ProfileList.ItemsSource = names.ToList();
            ProfileList.SelectedIndex = i;
        }
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        _profiles.Add(new ExportProfile { Name = $"Profile {_profiles.Count + 1}" });
        RefreshList(_profiles.Count - 1);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        var i = ProfileList.SelectedIndex;
        if (i < 0 || i >= _profiles.Count) return;
        _profiles.RemoveAt(i);
        RefreshList(Math.Min(i, _profiles.Count - 1));
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose output folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            FolderBox.Text = path;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
