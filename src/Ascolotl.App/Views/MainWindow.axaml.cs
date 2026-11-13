using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.ViewModels;

namespace Ascolotl.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private DocumentViewModel? _statusBarDoc;
    private bool _syncingScroll;
    private bool _editorPaneHooked;
    private ScrollViewer? _editorScrollViewer;
    private AvaloniaEdit.TextEditor? _cachedTextEditor;
    private PreviewView? _cachedPreviewView;
    private bool _pendingNewDocFocus;
    // Highest editor scroll offset we've seen where the preview was still
    // able to follow (i.e. didn't saturate at its max). Updated continuously
    // as the user scrolls; used to snap the editor back when it tries to
    // scroll further forward than the preview can reach. Reset whenever the
    // editor is rebound to a different document.
    private double _editorMaxValidOffset;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Zoom gestures: Ctrl+wheel (also what Windows sends for a touchpad
        // pinch) and the macOS touchpad magnify gesture. Tunnel, so the
        // editor and preview don't also scroll.
        AddHandler(PointerWheelChangedEvent, OnZoomWheel, RoutingStrategies.Tunnel);
        AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnZoomMagnify, RoutingStrategies.Tunnel);

        // When the window is maximized on Windows 11, the OS reserves a few
        // logical pixels at every edge for invisible resize-handle hit-testing.
        // With ExtendClientAreaToDecorationsHint that band sits INSIDE our
        // drawing area, so content drawn at the very edge (the header row at
        // top, the status bar at bottom) gets clipped. Compensate by insetting
        // the whole content by Window.OffScreenMargin while maximized.
        // WindowState and OffScreenMargin update on SEPARATE ticks during a
        // maximize: WindowState changes first, OffScreenMargin a moment later.
        // Both must trigger the update, else we read a stale (=0) margin.
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == OffScreenMarginProperty)
                UpdateMaximizeInsets();
        };
        UpdateMaximizeInsets();

        // Header drag region: empty space on the menu/tab row drags the
        // window, and a double-click toggles maximize (standard gestures).
        TitleDragArea.PointerPressed += OnTitleDragPressed;

        // Outline item double-click navigation
        OutlineList.DoubleTapped += OnOutlineItemDoubleTapped;

        // Sidebar section switcher + project tree navigation
        SidebarOutlineTab.PointerPressed += (_, _) => SetSidebarFilesActive(false);
        SidebarFilesTab.PointerPressed += (_, _) => SetSidebarFilesActive(true);
        ProjectTree.DoubleTapped += OnProjectTreeDoubleTapped;

        // Export profiles management
        ManageExportProfilesItem.Click += OnManageExportProfiles;

        // Tab middle-click
        DocumentTabStrip.PointerPressed += OnTabStripPointerPressed;

        // Command palette keyboard routing
        CommandPaletteFilter.KeyDown += OnCommandPaletteKeyDown;

        // Click outside command palette to close
        CommandPaletteBackdrop.PointerPressed += OnCommandPaletteBackdropPressed;

        // Syntax reference (F1) routing
        SyntaxReferenceFilter.KeyDown += OnSyntaxReferenceKeyDown;
        SyntaxReferenceBackdrop.PointerPressed += (_, e) =>
        {
            _viewModel?.SyntaxReference.Close();
            e.Handled = true;
        };
        SyntaxReferenceList.DoubleTapped += (_, _) => InsertSelectedSyntax();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.RecentFiles.CollectionChanged -= OnRecentFilesChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigateToLineRequested -= OnNavigateToLine;
            _viewModel.NewDocumentCreated -= OnNewDocumentCreated;
            _viewModel.ExportProfilesChanged -= RebuildExportMenu;
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.RecentFiles.CollectionChanged += OnRecentFilesChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.NavigateToLineRequested += OnNavigateToLine;
            _viewModel.NewDocumentCreated += OnNewDocumentCreated;
            RebuildRecentFilesMenu(_viewModel);
            UpdateEditorLayout(_viewModel.EditorMode);
            UpdateStatusBarExtras();
            ApplyMenuMode(_viewModel.HamburgerMenu);
            UpdateSidebarTabs();
            _viewModel.ExportProfilesChanged += RebuildExportMenu;
            RebuildExportMenu();
        }
    }

    /// <summary>
    /// Places File/View either nested under a single ☰ item (hamburger) or
    /// inline as top-level menu items, by reparenting the same MenuItem
    /// instances — no duplicated markup, so toggling is lossless.
    /// </summary>
    private void ApplyMenuMode(bool hamburger)
    {
        MainMenu.Items.Clear();
        HamburgerMenuItem.Items.Clear();

        if (hamburger)
        {
            HamburgerMenuItem.Items.Add(FileMenuItem);
            HamburgerMenuItem.Items.Add(ViewMenuItem);
            HamburgerMenuItem.Items.Add(StyleMenuItem);
            MainMenu.Items.Add(HamburgerMenuItem);
        }
        else
        {
            MainMenu.Items.Add(FileMenuItem);
            MainMenu.Items.Add(ViewMenuItem);
            MainMenu.Items.Add(StyleMenuItem);
        }
    }

    private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is not null)
            RebuildRecentFilesMenu(_viewModel);
    }

    // --- Export profiles ---

    private readonly List<Control> _exportProfileMenuItems = [];

    /// <summary>Profile entries live at the top of the Export submenu, above
    /// the raw format items, so the one-click path is the shortest one.</summary>
    private void RebuildExportMenu()
    {
        foreach (var item in _exportProfileMenuItems)
            ExportMenu.Items.Remove(item);
        _exportProfileMenuItems.Clear();

        if (_viewModel is null || _viewModel.ExportProfiles.Count == 0)
            return;

        var index = 0;
        foreach (var profile in _viewModel.ExportProfiles)
        {
            var item = new MenuItem
            {
                Header = profile.Name,
                Command = _viewModel.ExportWithProfileCommand,
                CommandParameter = profile,
            };
            ToolTip.SetTip(item,
                $"{profile.Format.DisplayName()} · {profile.Theme.DisplayName()} → {profile.OutputFolder ?? "next to the document"}");
            _exportProfileMenuItems.Add(item);
            ExportMenu.Items.Insert(index++, item);
        }

        var separator = new Separator();
        _exportProfileMenuItems.Add(separator);
        ExportMenu.Items.Insert(index, separator);
    }

    private async void OnManageExportProfiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var dialog = new ExportProfilesDialog(_viewModel.ExportProfiles);
        await dialog.ShowDialog(this);

        // The dialog edits the collection in place; persist + rebuild.
        _viewModel.NotifyExportProfilesChanged();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.PropertyName == nameof(MainWindowViewModel.EditorMode))
            UpdateEditorLayout(_viewModel.EditorMode);

        if (e.PropertyName is nameof(MainWindowViewModel.EditorMode)
            or nameof(MainWindowViewModel.SelectedDocument))
            UpdateStatusBarExtras();

        if (e.PropertyName == nameof(MainWindowViewModel.PreviewFitWidth) && _viewModel.SelectedDocument is { } fitDoc)
            ZoomText.Text = ZoomLabel(fitDoc);

        if (e.PropertyName == nameof(MainWindowViewModel.HamburgerMenu))
            ApplyMenuMode(_viewModel.HamburgerMenu);

        if (e.PropertyName is nameof(MainWindowViewModel.SidebarFilesActive)
            or nameof(MainWindowViewModel.HasProject))
            UpdateSidebarTabs();
    }

    // --- Project sidebar ---

    private void SetSidebarFilesActive(bool filesActive)
    {
        if (_viewModel is not null)
            _viewModel.SidebarFilesActive = filesActive;
    }

    /// <summary>The active sidebar tab reads full-strength; the inactive one
    /// stays muted — the same quiet emphasis the status bar uses.</summary>
    private void UpdateSidebarTabs()
    {
        if (_viewModel is null) return;

        Avalonia.Media.IBrush? Brush(string key) =>
            this.TryFindResource(key, ActualThemeVariant, out var v) ? v as Avalonia.Media.IBrush : null;

        var active = Brush("ForgeTextBrush");
        var muted = Brush("ForgeTextMutedBrush");
        if (active is null || muted is null) return;

        var files = _viewModel.SidebarFilesActive;
        SidebarOutlineTab.Foreground = files ? muted : active;
        SidebarFilesTab.Foreground = files ? active : muted;
    }

    private async void OnProjectTreeDoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        if (ProjectTree.SelectedItem is ProjectNode { IsDirectory: false } node)
            await _viewModel.OpenFileByPathAsync(node.FullPath);
    }

    private void UpdateMaximizeInsets()
    {
        // While maximized the OS reserves a few off-screen pixels at every edge
        // for the invisible resize band. With ExtendClientAreaToDecorationsHint
        // that band sits inside our drawing area, so content at the very edge —
        // the header row at top, the status bar at bottom — gets clipped. Inset
        // the whole content by OffScreenMargin to push it into the visible area.
        var maximized = WindowState == WindowState.Maximized;
        ContentDockPanel.Margin = maximized ? OffScreenMargin : default;
    }

    private void UpdateEditorLayout(EditorMode mode)
    {
        var cols = ContentGrid.ColumnDefinitions;

        switch (mode)
        {
            case EditorMode.Source:
                EditorPane.IsVisible = true;
                Splitter.IsVisible = false;
                PreviewPane.IsVisible = false;
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[1].Width = new GridLength(0);
                cols[2].Width = new GridLength(0);
                break;

            case EditorMode.Wysiwyg:
                // Preview-only: the source pane is hidden but stays in the
                // tree — it remains the single source of truth that in-place
                // block edits splice into.
                EditorPane.IsVisible = false;
                Splitter.IsVisible = false;
                PreviewPane.IsVisible = true;
                cols[0].Width = new GridLength(0);
                cols[1].Width = new GridLength(0);
                cols[2].Width = new GridLength(1, GridUnitType.Star);
                break;

            case EditorMode.Split:
            default:
                EditorPane.IsVisible = true;
                Splitter.IsVisible = true;
                PreviewPane.IsVisible = true;
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[1].Width = GridLength.Auto;
                cols[2].Width = new GridLength(1, GridUnitType.Star);
                break;
        }
    }

    private void UpdateStatusBarExtras()
    {
        if (_viewModel is null) return;

        EditorModeText.Text = _viewModel.EditorMode.ToString();

        // Unsubscribe from the previous document to avoid leak
        if (_statusBarDoc is not null)
        {
            _statusBarDoc.PropertyChanged -= OnSelectedDocPropertyChanged;
            _statusBarDoc = null;
        }

        var doc = _viewModel.SelectedDocument;
        if (doc is not null)
        {
            ZoomText.Text = ZoomLabel(doc);
            CursorPositionText.Text = $"Ln {doc.CursorLine}, Col {doc.CursorColumn}";

            _statusBarDoc = doc;
            _statusBarDoc.PropertyChanged += OnSelectedDocPropertyChanged;
        }
        else
        {
            ZoomText.Text = "100%";
            CursorPositionText.Text = "Ln 1, Col 1";
        }
    }

    private void OnSelectedDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentViewModel doc) return;

        if (e.PropertyName == nameof(DocumentViewModel.CursorLine)
            || e.PropertyName == nameof(DocumentViewModel.CursorColumn))
        {
            CursorPositionText.Text = $"Ln {doc.CursorLine}, Col {doc.CursorColumn}";
        }
        else if (e.PropertyName == nameof(DocumentViewModel.ZoomLevel))
        {
            ZoomText.Text = ZoomLabel(doc);
        }
    }

    private string ZoomLabel(DocumentViewModel doc)
        => _viewModel?.PreviewFitWidth == true ? "Fit" : $"{doc.ZoomLevel}%";

    private void RebuildRecentFilesMenu(MainWindowViewModel vm)
    {
        RecentFilesMenu.Items.Clear();

        foreach (var path in vm.RecentFiles)
        {
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => vm.OpenRecentFileCommand.Execute(path);
            RecentFilesMenu.Items.Add(item);
        }

        RecentFilesMenu.IsEnabled = vm.RecentFiles.Count > 0;
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    // --- Keyboard shortcuts ---

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is null || e.Handled) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && shift && e.Key == Key.P)
        {
            _viewModel.ToggleCommandPaletteCommand.Execute(null);
            if (_viewModel.CommandPalette.IsOpen)
                Dispatcher.UIThread.Post(() => CommandPaletteFilter.Focus(), DispatcherPriority.Input);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.T)
        {
            _viewModel.ReopenClosedTabCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.Tab)
        {
            _viewModel.PreviousTabCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.Tab)
        {
            _viewModel.NextTabCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.B)
        {
            _viewModel.ToggleOutlinePaneCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.CommandPalette.IsOpen)
        {
            _viewModel.CommandPalette.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F1 && e.KeyModifiers == KeyModifiers.None)
        {
            _viewModel.ToggleSyntaxReferenceCommand.Execute(null);
            if (_viewModel.SyntaxReference.IsOpen)
                Dispatcher.UIThread.Post(() => SyntaxReferenceFilter.Focus(), DispatcherPriority.Input);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.SyntaxReference.IsOpen)
        {
            _viewModel.SyntaxReference.Close();
            e.Handled = true;
        }
    }

    // --- Syntax reference routing ---

    private void OnSyntaxReferenceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.Key == Key.Enter)
        {
            InsertSelectedSyntax();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.SyntaxReference.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _viewModel.SyntaxReference.MoveUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _viewModel.SyntaxReference.MoveDown();
            e.Handled = true;
        }
    }

    /// <summary>Insert the selected reference entry's snippet at the source
    /// editor's caret (Source/Split); the reference stays a lookup tool in
    /// WYSIWYG, where there is no caret to target.</summary>
    private void InsertSelectedSyntax()
    {
        if (_viewModel?.SyntaxReference.SelectedItem is not { } item)
            return;

        _viewModel.SyntaxReference.Close();

        var editorView = FindDescendant<SourceEditorView>(EditorPane);
        if (editorView is null)
        {
            _viewModel.StatusText = "Switch to Source or Split mode to insert syntax";
            return;
        }

        var text = Ascolotl.Core.Editing.Assist.Snippet.Parse(item.Snippet).Text;
        editorView.InsertAtCaret(text);
    }

    // --- Zoom gestures ---

    // Touchpads send many small deltas; step one zoom notch per whole unit.
    private double _zoomWheelAccum;
    private double _zoomMagnifyAccum;

    private void OnZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        _zoomWheelAccum = StepZoom(_zoomWheelAccum + e.Delta.Y, 1.0);
    }

    private void OnZoomMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_viewModel is null) return;
        e.Handled = true;
        _zoomMagnifyAccum = StepZoom(_zoomMagnifyAccum + e.Delta.X, 0.1);
    }

    /// <summary>Zooms one step per <paramref name="unit"/> accumulated; returns the remainder.</summary>
    private double StepZoom(double accum, double unit)
    {
        while (accum >= unit) { _viewModel!.ZoomInCommand.Execute(null); accum -= unit; }
        while (accum <= -unit) { _viewModel!.ZoomOutCommand.Execute(null); accum += unit; }
        return accum;
    }

    // --- Command palette keyboard routing ---

    private void OnCommandPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.Key == Key.Enter)
        {
            _viewModel.CommandPalette.ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.CommandPalette.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _viewModel.CommandPalette.MoveUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _viewModel.CommandPalette.MoveDown();
            e.Handled = true;
        }
    }

    private void OnCommandPaletteBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel?.CommandPalette.Close();
        e.Handled = true;
    }

    // --- DragDrop ---

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_viewModel is null) return;

#pragma warning disable CS0618 // Data is obsolete, but DataTransfer API differs
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files is null) return;

        foreach (var item in files)
        {
            var path = item.Path?.LocalPath;
            if (path is null) continue;

            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".adoc" or ".asciidoc" or ".txt" or ".asc")
            {
                await _viewModel.OpenFileByPathAsync(path);
            }
        }
    }

    // --- Tab middle-click ---

    private void OnTabStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null) return;

        var point = e.GetCurrentPoint(DocumentTabStrip);
        if (!point.Properties.IsMiddleButtonPressed) return;

        // Find which tab item was clicked
        var source = e.Source as Visual;
        while (source is not null && source != DocumentTabStrip)
        {
            if (source.DataContext is DocumentViewModel doc)
            {
                _viewModel.CloseDocumentCommand.Execute(doc);
                e.Handled = true;
                return;
            }
            source = source.GetVisualParent();
        }
    }

    // --- Outline navigation ---

    private void OnOutlineItemDoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        if (OutlineList.SelectedItem is OutlineItem item)
            _viewModel.NavigateToOutlineItemCommand.Execute(item);
    }

    private void OnNavigateToLine(int lineNumber)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var editorView = FindDescendant<SourceEditorView>(EditorPane);
            editorView?.ScrollToLine(lineNumber);
        }, DispatcherPriority.Background);
    }

    // After a new document is created, give it focus so the first keystroke
    // lands in it: the editor pane for Source/Split, or a fresh editable
    // paragraph block in the WYSIWYG preview.
    private void OnNewDocumentCreated()
    {
        if (_viewModel is null) return;

        if (_viewModel.EditorMode == EditorMode.Wysiwyg)
        {
            Dispatcher.UIThread.Post(
                () => FindDescendant<PreviewView>(PreviewPane)?.StartFreshParagraphEdit(),
                DispatcherPriority.Background);
            return;
        }

        // Source / Split: the new document's editor view is created
        // asynchronously, so a Background post fires too early (the editor isn't
        // wired up yet, especially for the first doc from the empty state).
        // Defer to HookEditorScroll, which runs once the editor pane is ready.
        _pendingNewDocFocus = true;
    }

    // --- Scroll synchronization ---

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (!_editorPaneHooked)
        {
            EditorPane.PropertyChanged += OnEditorPanePropertyChanged;
            _editorPaneHooked = true;
        }
        Dispatcher.UIThread.Post(HookEditorScroll, DispatcherPriority.Loaded);
    }

    private void OnEditorPanePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ContentControl.ContentProperty)
            Dispatcher.UIThread.Post(HookEditorScroll, DispatcherPriority.Loaded);
    }

    private void HookEditorScroll()
    {
        // Unhook previous
        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ScrollChanged -= OnEditorScrollChanged;
            _editorScrollViewer = null;
        }
        if (_cachedPreviewView is not null)
        {
            _cachedPreviewView.SourceLineRequested -= OnPreviewSourceLineRequested;
            _cachedPreviewView.SourceLineClicked -= OnPreviewSourceLineClicked;
        }

        // New editor binding — discard any saturation cap that referred to
        // the previous document's metrics.
        _editorMaxValidOffset = 0;

        var editorView = FindDescendant<SourceEditorView>(EditorPane);
        if (editorView is null) return;

        _cachedTextEditor = editorView.FindControl<AvaloniaEdit.TextEditor>("Editor");
        if (_cachedTextEditor is null) return;

        // Focus a freshly created document's editor for the first keystroke
        // (queued by OnNewDocumentCreated). Defer one more pass so the TextArea
        // is fully loaded before it takes keyboard focus.
        if (_pendingNewDocFocus)
        {
            _pendingNewDocFocus = false;
            var ev = editorView;
            Dispatcher.UIThread.Post(ev.FocusEditor, DispatcherPriority.Input);
        }

        _editorScrollViewer = FindDescendant<ScrollViewer>(_cachedTextEditor);
        if (_editorScrollViewer is not null)
            _editorScrollViewer.ScrollChanged += OnEditorScrollChanged;

        _cachedPreviewView = FindDescendant<PreviewView>(PreviewPane);
        if (_cachedPreviewView is not null)
        {
            _cachedPreviewView.SourceLineRequested += OnPreviewSourceLineRequested;
            _cachedPreviewView.SourceLineClicked += OnPreviewSourceLineClicked;
        }
    }

    /// <summary>
    /// Click-to-source: user clicked rendered preview content resolving to
    /// editor (<paramref name="line"/>, <paramref name="column"/>) — exact
    /// when 1.0.9 inline source ranges resolved the click, line-only
    /// (column 0) on the line-map fallback. Scroll the editor so that source
    /// line's top edge lands at the click's viewport-Y, so the clicked line
    /// sits visually across from where it is in the preview, and place the
    /// caret at the resolved column. Focus stays put (the user is in the
    /// preview).
    /// </summary>
    private void OnPreviewSourceLineClicked(int line, int column, double clickViewportY)
    {
        if (_viewModel is null || _viewModel.EditorMode != EditorMode.Split)
            return;
        if (_cachedTextEditor is null || _editorScrollViewer is null)
            return;

        var doc = _cachedTextEditor.Document;
        if (doc is null || doc.LineCount <= 0) return;

        var textView = _cachedTextEditor.TextArea.TextView;
        var lineHeight = textView.DefaultLineHeight;
        var wordWrap = _cachedTextEditor.WordWrap;
        var maxLine = doc.LineCount - 1;
        var clampedLine = Math.Clamp(line, 0, maxLine);

        double lineContentY;
        if (wordWrap)
        {
            var dl = doc.GetLineByNumber(clampedLine + 1);
            lineContentY = textView.GetVisualTopByDocumentLine(dl.LineNumber);
        }
        else
        {
            lineContentY = clampedLine * lineHeight;
        }

        var max = Math.Max(0, _editorScrollViewer.Extent.Height - _editorScrollViewer.Viewport.Height);
        var targetY = Math.Clamp(lineContentY - clickViewportY, 0, max);

        _syncingScroll = true;
        if (_cachedPreviewView is not null) _cachedPreviewView.SuppressReverseSync = true;
        try
        {
            _editorScrollViewer.Offset = new Vector(_editorScrollViewer.Offset.X, targetY);

            // Place the caret at the resolved column, clamped to the line's
            // length. column<=0 (line-map fallback) lands at the line start.
            var docLine = doc.GetLineByNumber(clampedLine + 1);
            var caretOffset = docLine.Offset + Math.Clamp(column, 0, docLine.Length);
            _cachedTextEditor.CaretOffset = caretOffset;
            _editorMaxValidOffset = targetY;
        }
        finally
        {
            ClearSyncFlagsAfterLayout();
        }
    }

    /// <summary>
    /// Reverse sync: the preview was scrolled or clicked, requesting the
    /// editor's caret/scroll to move to <paramref name="line"/>. Scrolls the
    /// editor's first visible line to match, using visual line positions
    /// when word-wrap is on so wrapped paragraphs land at the right pixel.
    /// </summary>
    private void OnPreviewSourceLineRequested(int line, double fraction)
    {
        if (_syncingScroll || _viewModel is null || !_viewModel.SyncScrolling)
            return;
        if (_viewModel.EditorMode != EditorMode.Split) return;
        if (_cachedTextEditor is null || _editorScrollViewer is null) return;

        var doc = _cachedTextEditor.Document;
        if (doc is null || doc.LineCount <= 0) return;

        var textArea = _cachedTextEditor.TextArea;
        var textView = textArea.TextView;
        var wordWrap = _cachedTextEditor.WordWrap;
        var lineHeight = textView.DefaultLineHeight;
        var clampedLine = Math.Clamp(line, 0, doc.LineCount - 1);

        double targetY;
        if (wordWrap)
        {
            // 1-based line number for AvaloniaEdit; first visible row for the
            // requested document line, plus the sub-line fraction times the
            // line height. Visual top is in document content space.
            var docLine = doc.GetLineByNumber(clampedLine + 1);
            targetY = textView.GetVisualTopByDocumentLine(docLine.LineNumber)
                      + Math.Clamp(fraction, 0.0, 1.0) * lineHeight;
        }
        else
        {
            targetY = (clampedLine + Math.Clamp(fraction, 0.0, 1.0)) * lineHeight;
        }

        // Clamp into the editor's valid scroll range.
        var max = Math.Max(0, _editorScrollViewer.Extent.Height - _editorScrollViewer.Viewport.Height);
        targetY = Math.Clamp(targetY, 0, max);

        // While we drive the editor, suppress both sides so the
        // ScrollChanged events the assignment triggers don't bounce back
        // and start a feedback loop. The reset has to wait until AFTER the
        // resulting layout pass fires its ScrollChanged — clearing
        // synchronously in `finally` happens before those events fire and
        // breaks the suppression.
        _syncingScroll = true;
        if (_cachedPreviewView is not null) _cachedPreviewView.SuppressReverseSync = true;
        _editorScrollViewer.Offset = new Vector(_editorScrollViewer.Offset.X, targetY);
        ClearSyncFlagsAfterLayout();

        // Also update the editor's max-valid offset so the saturation clamp
        // doesn't snap the user back when they next scroll the editor.
        _editorMaxValidOffset = targetY;
    }

    /// <summary>
    /// Posts a low-priority callback that clears the cross-pane sync flags
    /// after the next layout / ScrollChanged round has fired. Single source
    /// of truth for closing a sync transaction in either direction.
    /// </summary>
    private void ClearSyncFlagsAfterLayout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _syncingScroll = false;
            if (_cachedPreviewView is not null)
                _cachedPreviewView.SuppressReverseSync = false;
        }, DispatcherPriority.Background);
    }

    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll || _viewModel is null || !_viewModel.SyncScrolling)
            return;
        if (_viewModel.EditorMode != EditorMode.Split)
            return;

        var sv = sender as ScrollViewer;
        if (sv is null || _cachedTextEditor is null) return;

        var doc = _cachedTextEditor.Document;
        if (doc is null || doc.LineCount <= 0) return;

        var textArea = _cachedTextEditor.TextArea;
        var textView = textArea.TextView;
        var wordWrap = _cachedTextEditor.WordWrap;
        var lineHeight = textView.DefaultLineHeight;
        var totalLines = doc.LineCount;

        // Build visual line starts array when word wrap is active
        double[]? visualLineStarts = null;
        if (wordWrap && totalLines > 0)
        {
            visualLineStarts = new double[totalLines];
            for (var i = 0; i < totalLines; i++)
            {
                var docLine = doc.GetLineByNumber(i + 1); // 1-based
                var visualTop = textView.GetVisualTopByDocumentLine(docLine.LineNumber);
                visualLineStarts[i] = visualTop;
            }
        }

        var (line, frac) = ScrollSyncCalculator.GetFirstVisibleLine(
            sv.Offset.Y, lineHeight, totalLines, wordWrap, visualLineStarts);

        _syncingScroll = true;
        // Also suppress reverse sync while we drive the preview — the
        // preview's resulting ScrollChanged fires AFTER this handler returns
        // (next layout pass). Hold both flags until that pass has settled
        // via ClearSyncFlagsAfterLayout below.
        if (_cachedPreviewView is not null) _cachedPreviewView.SuppressReverseSync = true;
        try
        {
            var canFollow = SyncPreviewToLine(line, frac, totalLines);
            if (canFollow)
            {
                // Preview reached the requested position. Remember this
                // editor offset as the furthest we've successfully synced.
                _editorMaxValidOffset = sv.Offset.Y;
            }
            else if (sv.Offset.Y > _editorMaxValidOffset)
            {
                // Preview is already at its end — scrolling the editor
                // further would visibly desync the panes. Snap the editor
                // back to the last offset where the preview could still
                // follow.
                sv.Offset = new Vector(sv.Offset.X, _editorMaxValidOffset);
            }
            else
            {
                // User scrolled backward past a saturation point; the new
                // offset is below the cap, so accept it as the new cap.
                _editorMaxValidOffset = sv.Offset.Y;
            }
        }
        finally
        {
            ClearSyncFlagsAfterLayout();
        }
    }

    private bool SyncPreviewToLine(int line, double frac, int totalLines)
    {
        if (_cachedPreviewView is null) return true;
        return _cachedPreviewView.SyncToSourceLine(line, frac, totalLines);
    }

    private static T? FindDescendant<T>(Visual visual) where T : Visual
    {
        foreach (var child in visual.GetVisualChildren())
        {
            if (child is T match) return match;
            var found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}
