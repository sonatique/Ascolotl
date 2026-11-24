using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Ascolotl.App.Views.Assist;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.ViewModels;
using Ascolotl.Core.Resources;

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

    static MainWindow()
    {
        // Screen readers say a menu item's key as the menu shows it ("Ctrl+/"), not by its key name
        // ("Ctrl+OemQuestion"): every menu item, the application's and the page's.
        MenuItem.InputGestureProperty.Changed.AddClassHandler<MenuItem>((item, _) =>
        {
            if (item.InputGesture is { } key) Avalonia.Automation.AutomationProperties.SetAcceleratorKey(item, key.ToString("p", null));
            else item.ClearValue(Avalonia.Automation.AutomationProperties.AcceleratorKeyProperty);
        });
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(DragDrop.DropEvent, OnDrop);

        // The toolbar formats the editor that last had the keyboard.
        FormatBar.FormatRequested += OnFormatRequested;
        FormatBar.TableRequested += op =>
        {
            if (FormatTarget() is { } target && CompletionController.For(target) is { } assist) assist.Table(op);
            else if (_viewModel is not null) _viewModel.StatusText = Strings.StatusClickTextToFormat;
        };
        FormatBar.EditActionRequested += apply =>
            (apply ? _viewModel?.FlushPendingEdits : _viewModel?.CancelPendingEdits)?.Invoke();
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        // Before the editor, which binds some of them itself (Ctrl+I indents).
        AddHandler(KeyDownEvent, OnFormatKey, RoutingStrategies.Tunnel);

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
        AppIcon.PointerPressed += OnAppIconPressed;

        // The view switch shows its shortcuts: key names, nothing to translate. Ctrl+1 to 4 go by the views'
        // order (Source, Split, WYSIWYG, Read), whatever the buttons' order.
        foreach (var button in ModeSwitch.Children.OfType<Button>())
            ToolTip.SetTip(button, CommandKey.Text($"Ctrl+{(int)(EditorMode)button.CommandParameter! + 1}"));

        // A click on an outline row goes to its section.
        OutlineList.Tapped += OnOutlineItemTapped;

        // A sidebar dragged wide stays inside a narrower window, and widens again with it.
        OuterGrid.SizeChanged += (_, e) => OuterGrid.ColumnDefinitions[2].MaxWidth =
            Math.Max(120, e.NewSize.Width - OuterGrid.ColumnDefinitions[0].MinWidth - Math.Max(SidebarSplitter.Width, SidebarSplitter.MinWidth));

        // Sidebar section switcher + project tree navigation
        SidebarOutlineTab.PointerPressed += (_, _) => SetSidebarFilesActive(false);
        SidebarFilesTab.PointerPressed += (_, _) => SetSidebarFilesActive(true);
        ProjectTree.DoubleTapped += OnProjectTreeDoubleTapped;
        // The same from the keyboard: Enter switches, goes to a section or opens a file; Esc goes back.
        SidebarOutlineTab.KeyDown += (_, e) => OnSidebarKey(e, () => SetSidebarFilesActive(false), space: true);
        SidebarFilesTab.KeyDown += (_, e) => OnSidebarKey(e, () => SetSidebarFilesActive(true), space: true);
        OutlineList.KeyDown += (_, e) => OnSidebarKey(e, () => _viewModel?.NavigateToOutlineItemCommand.Execute(OutlineList.SelectedItem));
        ProjectTree.KeyDown += (_, e) => OnSidebarKey(e, () => OnProjectTreeDoubleTapped(null, e));

        // The menu-bar set of the application menus (the hamburger holds the
        // other). Its menus join the bar here, before they've ever been shown.
        var bar = new AppMenus();
        foreach (var menu in bar.Items.OfType<MenuItem>().ToList())
        {
            bar.Items.Remove(menu);
            MainMenu.Items.Add(menu);
            _barMenus.Add(menu);
        }
        _menuSets = [HamburgerMenuItem, bar];
        // The menus' keys are written with Ctrl: on a Mac, Cmd.
        if (CommandKey.Modifier != KeyModifiers.Control)
            foreach (var item in _barMenus.Prepend<MenuItem>(HamburgerMenuItem).SelectMany(m => m.GetLogicalDescendants().Prepend(m)).OfType<MenuItem>())
                if (item.InputGesture is { } written) item.InputGesture = CommandKey.OnPlatform(written);

        // Export profiles management
        foreach (var set in _menuSets)
            set.ManageExportProfilesItem.Click += OnManageExportProfiles;

        // Tab middle-click
        DocumentTabStrip.PointerPressed += OnTabStripPointerPressed;

        // Many tabs: the wheel scrolls the strip, and the all-tabs button
        // appears once the tabs no longer fit.
        TabScroll.AddHandler(PointerWheelChangedEvent, OnTabStripWheel, RoutingStrategies.Tunnel);
        TabScroll.ScrollChanged += (_, _) => AllTabsButton.IsVisible = TabScroll.Extent.Width > TabScroll.Viewport.Width + 1;
        AllTabsButton.Click += OnAllTabsClick;

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
            _viewModel.SwitchPaneRequested -= OnSwitchPaneRequested;
            _viewModel.CommandPalette.PropertyChanged -= OnOverlayChanged;
            _viewModel.SyntaxReference.PropertyChanged -= OnOverlayChanged;
            _viewModel.ExportProfilesChanged -= RebuildExportMenu;
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.RecentFiles.CollectionChanged += OnRecentFilesChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.NavigateToLineRequested += OnNavigateToLine;
            _viewModel.NewDocumentCreated += OnNewDocumentCreated;
            _viewModel.SwitchPaneRequested += OnSwitchPaneRequested;
            _viewModel.CommandPalette.PropertyChanged += OnOverlayChanged;
            _viewModel.SyntaxReference.PropertyChanged += OnOverlayChanged;
            RebuildRecentFilesMenu(_viewModel);
            UpdateEditorLayout(_viewModel.EditorMode);
            UpdateSidebarColumn();
            UpdateStatusBarExtras();
            ApplyMenuMode(_viewModel.HamburgerMenu);
            UpdateSidebarTabs();
            _viewModel.ExportProfilesChanged += RebuildExportMenu;
            RebuildExportMenu();
        }
    }

    // The application menus exist twice (see AppMenus): inside the hamburger and
    // as the menu bar. Showing one set never moves menus between parents, which
    // Avalonia can't do safely once a menu has been opened.
    private readonly List<MenuItem> _barMenus = [];
    private AppMenus[] _menuSets = [];

    private void ApplyMenuMode(bool hamburger)
    {
        // The switch is clicked inside an open menu: close it first.
        MainMenu.Close();
        HamburgerMenuItem.IsVisible = hamburger;
        foreach (var menu in _barMenus)
            menu.IsVisible = !hamburger;
    }

    private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is not null)
            RebuildRecentFilesMenu(_viewModel);
    }

    // --- Export profiles ---

    private readonly Dictionary<MenuItem, List<Control>> _exportProfileMenuItems = [];

    /// <summary>Profile entries live at the top of the Export submenu, above
    /// the raw format items, so the one-click path is the shortest one.</summary>
    private void RebuildExportMenu()
    {
        foreach (var set in _menuSets)
            RebuildExportMenu(set.ExportMenu);
    }

    private void RebuildExportMenu(MenuItem exportMenu)
    {
        if (_exportProfileMenuItems.TryGetValue(exportMenu, out var previous))
            foreach (var item in previous)
                exportMenu.Items.Remove(item);
        var added = _exportProfileMenuItems[exportMenu] = [];

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
                $"{profile.Format.DisplayName()} · {profile.Theme.DisplayName()} → {profile.OutputFolder ?? Strings.ProfileNextToDocument}");
            added.Add(item);
            exportMenu.Items.Insert(index++, item);
        }

        var separator = new Separator();
        added.Add(separator);
        exportMenu.Items.Insert(index, separator);
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
        {
            UpdateEditorLayout(_viewModel.EditorMode);
            // A pane the old mode hid only gets its view now: hook it for scroll sync.
            if (_editorScrollViewer is null || _cachedPreviewView is null)
                Dispatcher.UIThread.Post(HookEditorScroll, DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.ShowOutlinePane))
        {
            UpdateSidebarColumn();
            Dispatcher.UIThread.Post(FocusSidebarOrDocument, DispatcherPriority.Background);
        }

        if (e.PropertyName is nameof(MainWindowViewModel.EditorMode) or nameof(MainWindowViewModel.InPlaceEditOpen)
            or nameof(MainWindowViewModel.SelectedDocument))
            Dispatcher.UIThread.Post(UpdateTableTools); // after the layout the change brings

        if (e.PropertyName is nameof(MainWindowViewModel.EditorMode)
            or nameof(MainWindowViewModel.SelectedDocument))
        {
            UpdateStatusBarExtras();
            Dispatcher.UIThread.Post(GiveThePageAnIdleKeyboard, DispatcherPriority.Background);
        }

        // Keep the selected tab in view when the tabs overflow.
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedDocument) && _viewModel.SelectedDocument is { } shown)
            Dispatcher.UIThread.Post(() => DocumentTabStrip.ContainerFromItem(shown)?.BringIntoView());

        if (e.PropertyName == nameof(MainWindowViewModel.PreviewFitWidth) && _viewModel.SelectedDocument is { } fitDoc)
            ZoomText.Text = ZoomLabel(fitDoc);

        if (e.PropertyName == nameof(MainWindowViewModel.HamburgerMenu))
            ApplyMenuMode(_viewModel.HamburgerMenu);

        if (e.PropertyName is nameof(MainWindowViewModel.SidebarFilesActive)
            or nameof(MainWindowViewModel.HasProject))
            UpdateSidebarTabs();
    }

    // --- Project sidebar ---

    private void OnSidebarKey(KeyEventArgs e, Action enter, bool space = false)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter || space && e.Key == Key.Space) enter();
        else if (e.Key == Key.Escape) FocusDocument();
        else return;
        e.Handled = true;
    }

    /// <summary>The sidebar takes the keyboard as it opens, and hands it back to the document as it closes.</summary>
    private void FocusSidebarOrDocument()
    {
        if (_viewModel?.ShowOutlinePane == true) FocusSidebar();
        else if (FocusManager?.GetFocusedElement() is null or Visual { IsEffectivelyVisible: false }) FocusDocument();
    }

    /// <summary>The sidebar's selected row, or its first one.</summary>
    private void FocusSidebar()
    {
        var files = _viewModel?.SidebarFilesActive == true;
        var row = files
            ? ProjectTree.SelectedItem is { } file ? ProjectTree.TreeContainerFromItem(file) : ProjectTree.ContainerFromIndex(0)
            : OutlineList.ContainerFromIndex(Math.Max(0, OutlineList.SelectedIndex));
        (row ?? (files ? ProjectTree : OutlineList)).Focus(NavigationMethod.Tab);
    }

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

    // The source editor, or an in-place block editor in the preview.
    private TextEditor? _formatTarget;

    private void OnAnyGotFocus(object? sender, GotFocusEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TextEditor>(includeSelf: true) is { } editor
            && CompletionController.For(editor) is not null)
            _formatTarget = editor;
        if (e.Source is Visual focused && Regions.FirstOrDefault(r => r.IsVisualAncestorOf(focused)) is { } region)
        {
            _lastRegion = region;
            if (region != OutlinePane) _pageHadKeyboard = region == PreviewPane;
        }
        UpdateTableTools();
    }

    // F6's round: the source, the page and the sidebar, those shown.
    private Control[] Regions => [EditorPane, PreviewPane, OutlinePane];

    // Where the keyboard was last: the menu or the palette has it when their F6 runs.
    private Control? _lastRegion;
    private bool _pageHadKeyboard; // of the source and the page

    private void OnSwitchPaneRequested() =>
        Dispatcher.UIThread.Post(() => SwitchPane(_lastRegion), DispatcherPriority.Input); // once the menu has closed

    /// <summary>F6 (Shift+F6 backwards): the keyboard goes on to the next of the source, the page and the sidebar.</summary>
    private void SwitchPane(Control? from, int step = 1)
    {
        var shown = Regions.Where(r => r.IsVisible).ToList();
        if (shown.Count == 0) return;
        var at = shown.IndexOf(from!);
        if (at < 0) at = step > 0 ? -1 : 0; // from elsewhere: the first, or backwards the last
        var next = shown[(at + step + shown.Count) % shown.Count];
        if (next == OutlinePane) FocusSidebar();
        else FocusDocument(page: next == PreviewPane);
    }

    /// <summary>The source or the page, the one the keyboard was last in unless told.</summary>
    private void FocusDocument(bool? page = null)
    {
        if (PreviewPane.IsVisible && ((page ?? _pageHadKeyboard) || !EditorPane.IsVisible))
            FindDescendant<PreviewView>(PreviewPane)?.FocusPage();
        else
            FindDescendant<SourceEditorView>(EditorPane)?.Editor.TextArea.Focus();
    }

    /// <summary>The editor the toolbar acts on. A closed in-place editor has left
    /// the window; the split view falls back to its source editor.</summary>
    private TextEditor? FormatTarget()
        => _formatTarget is { IsEffectivelyVisible: true } last && last.GetVisualRoot() == this ? last
            : _viewModel?.EditorMode == EditorMode.Split ? EditorPane.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault()
            : null;

    private TextEditor? _caretWatched;

    /// <summary>Show the table tools while the toolbar's editor has its caret in a table.</summary>
    private void UpdateTableTools()
    {
        var target = FormatTarget();
        if (_caretWatched != target)
        {
            if (_caretWatched is not null) _caretWatched.TextArea.Caret.PositionChanged -= OnTargetCaretMoved;
            _caretWatched = target;
            if (target is not null) target.TextArea.Caret.PositionChanged += OnTargetCaretMoved;
        }
        FormatBar.InTable = target is not null && CompletionController.For(target)?.InTable == true;
    }

    private void OnTargetCaretMoved(object? sender, EventArgs e) => UpdateTableTools();

    private void OnFormatRequested(FormatCommand command)
    {
        if (_viewModel is null) return;
        var target = FormatTarget();
        if (target is null || CompletionController.For(target) is not { } assist)
        {
            _viewModel.StatusText = Strings.StatusClickTextToFormat;
            return;
        }
        assist.Format(command);
    }

    private GridLength _sidebarWidth = new(220);

    /// <summary>The sidebar's width, also while it is hidden; App keeps it across runs.</summary>
    public double SidebarWidth
    {
        get => GivenWidth(OuterGrid.ColumnDefinitions[2]) is > 0 and var w ? w : _sidebarWidth.Value;
        set
        {
            if (value is not (>= 120 and <= 4000)) return; // the settings file can hold anything
            _sidebarWidth = new GridLength(value);
            var col = OuterGrid.ColumnDefinitions[2];
            if (col.Width.Value > 0) col.Width = _sidebarWidth;
        }
    }

    // The width the splitter gave the column, not what a narrow window leaves of it.
    private static double GivenWidth(ColumnDefinition col) => col.Width.IsAbsolute ? col.Width.Value : col.ActualWidth;

    // The column holds the sidebar's width, so the splitter resizes it; a hidden sidebar takes none.
    private void UpdateSidebarColumn()
    {
        var col = OuterGrid.ColumnDefinitions[2];
        if (GivenWidth(col) is > 0 and var w) _sidebarWidth = new GridLength(w);
        var show = _viewModel?.ShowOutlinePane == true;
        col.MinWidth = show ? 120 : 0;
        col.Width = show ? _sidebarWidth : new GridLength(0);
    }

    private void UpdateEditorLayout(EditorMode mode)
    {
        var cols = ContentGrid.ColumnDefinitions;
        // Hidden under the page, the source kept no position of its own: the page's is the tab's.
        if (mode is not (EditorMode.Wysiwyg or EditorMode.Read) && !EditorPane.IsVisible)
            FindDescendant<SourceEditorView>(EditorPane)?.ShowTopLine();

        switch (mode)
        {
            case EditorMode.Source:
                // The page is hidden: the keyboard goes to the source.
                if (PreviewPane.IsKeyboardFocusWithin)
                    FindDescendant<SourceEditorView>(EditorPane)?.Editor.TextArea.Focus();
                EditorPane.IsVisible = true;
                Splitter.IsVisible = false;
                PreviewPane.IsVisible = false;
                cols[0].Width = new GridLength(1, GridUnitType.Star);
                cols[1].Width = new GridLength(0);
                cols[2].Width = new GridLength(0);
                break;

            case EditorMode.Wysiwyg:
            case EditorMode.Read: // the same page, to read
                // Preview-only: the source pane is hidden but stays in the
                // tree — it remains the single source of truth that in-place
                // block edits splice into. It can't keep the keyboard: the page takes it.
                if (EditorPane.IsKeyboardFocusWithin)
                    FindDescendant<PreviewView>(PreviewPane)?.FocusPage();
                EditorPane.IsVisible = false;
                // Built even if never shown: it holds the tab's history, which Ctrl+Z on the page undoes.
                EditorPane.ApplyTemplate();
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
            CursorPositionText.Text = string.Format(Strings.CursorPosition, doc.CursorLine, doc.CursorColumn);

            _statusBarDoc = doc;
            _statusBarDoc.PropertyChanged += OnSelectedDocPropertyChanged;
        }
        else
        {
            ZoomText.Text = "100%";
            CursorPositionText.Text = Strings.Ln1Col1;
        }
    }

    private void OnSelectedDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentViewModel doc) return;

        if (e.PropertyName == nameof(DocumentViewModel.CursorLine)
            || e.PropertyName == nameof(DocumentViewModel.CursorColumn))
        {
            CursorPositionText.Text = string.Format(Strings.CursorPosition, doc.CursorLine, doc.CursorColumn);
        }
        else if (e.PropertyName == nameof(DocumentViewModel.ZoomLevel))
        {
            ZoomText.Text = ZoomLabel(doc);
        }
    }

    private string ZoomLabel(DocumentViewModel doc)
        => _viewModel?.PreviewFitWidth == true ? Strings.ZoomFit : $"{doc.ZoomLevel}%";

    private void RebuildRecentFilesMenu(MainWindowViewModel vm)
    {
        foreach (var set in _menuSets)
        {
            var recent = set.RecentFilesMenu;
            recent.Items.Clear();
            foreach (var path in vm.RecentFiles)
            {
                var item = new MenuItem { Header = path };
                item.Click += (_, _) => vm.OpenRecentFileCommand.Execute(path);
                recent.Items.Add(item);
            }
            recent.IsEnabled = vm.RecentFiles.Count > 0;
        }
    }

    /// <summary>A click on the app icon opens the window's system menu (Restore, Move, Size, Minimize,
    /// Maximize, Close) under it, as on any Windows window. The title bar is ours, and Avalonia keeps
    /// the system menu's own message (it gives Alt to the app's menus), so the menu is shown here.</summary>
    private void OnAppIconPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || TryGetPlatformHandle()?.Handle is not { } hwnd) return;
        e.Handled = true;
        var at = AppIcon.PointToScreen(new Point(0, AppIcon.Bounds.Height + 4));
        Dispatcher.UIThread.Post(() => ShowSystemMenu(hwnd, at)); // out of the press: the menu runs its own loop
    }

    private void ShowSystemMenu(IntPtr hwnd, PixelPoint at)
    {
        const int ScSize = 0xF000, ScMove = 0xF010, ScMinimize = 0xF020, ScMaximize = 0xF030, ScRestore = 0xF120;
        const uint MfGrayed = 1, TpmReturnCmd = 0x0100, WmSysCommand = 0x0112;
        var menu = GetSystemMenu(hwnd, false);
        if (menu == IntPtr.Zero) return;
        // Windows greys what the window's state rules out; for a menu shown by hand, so must we.
        var maximized = WindowState == WindowState.Maximized;
        void Enable(int command, bool on) => EnableMenuItem(menu, (uint)command, on ? 0 : MfGrayed);
        Enable(ScRestore, maximized);
        Enable(ScMove, !maximized);
        Enable(ScSize, !maximized && CanResize);
        Enable(ScMinimize, true);
        Enable(ScMaximize, !maximized && CanResize);
        var command = TrackPopupMenu(menu, TpmReturnCmd, at.X, at.Y, 0, hwnd, IntPtr.Zero);
        if (command != 0) PostMessage(hwnd, WmSysCommand, command, 0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int EnableMenuItem(IntPtr menu, uint item, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, nint wParam, nint lParam);

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

    // Where the keyboard was when the palette or the reference opened.
    private IInputElement? _beforeOverlay;

    /// <summary>The command palette or the syntax reference takes the keyboard in its search box as it opens,
    /// from its key, the menu or the palette, and gives it back as it closes, unless a command took it.</summary>
    private void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen) || _viewModel is null) return;
        var focused = FocusManager?.GetFocusedElement();
        var inOverlay = focused == CommandPaletteFilter || focused == SyntaxReferenceFilter;
        if (sender == _viewModel.CommandPalette ? _viewModel.CommandPalette.IsOpen : _viewModel.SyntaxReference.IsOpen)
        {
            if (!inOverlay) _beforeOverlay = focused;
            var filter = sender == _viewModel.CommandPalette ? CommandPaletteFilter : SyntaxReferenceFilter;
            Dispatcher.UIThread.Post(() => filter.Focus(), DispatcherPriority.Input);
        }
        else if (!_viewModel.CommandPalette.IsOpen && !_viewModel.SyntaxReference.IsOpen)
        {
            var back = _beforeOverlay;
            _beforeOverlay = null;
            if (inOverlay || focused is null or Visual { IsEffectivelyVisible: false })
            {
                if (back is InputElement { IsEffectivelyVisible: true } was && was.GetVisualRoot() == this) was.Focus();
                else FocusDocument();
            }
        }
    }

    // --- Keyboard shortcuts ---

    /// <summary>Bold, italic, inline code and link, as the toolbar's tooltips say, in the source or an open block.</summary>
    private void OnFormatKey(object? sender, KeyEventArgs e)
    {
        if (Enum.GetValues<FormatCommand>().Where(c => FormatToolbar.KeyFor(c)?.Matches(e) == true).Cast<FormatCommand?>().FirstOrDefault() is { } format
            && format is not (FormatCommand.Heading1 or FormatCommand.Heading2 or FormatCommand.Heading3) // the headings' own handling
            && (e.Source as Visual)?.FindAncestorOfType<TextEditor>(includeSelf: true) is { } editor
            && CompletionController.For(editor) is { } assist)
        {
            assist.Format(format);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is null || e.Handled) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && shift && e.Key == Key.Tab)
        {
            _viewModel.PreviousTabCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.Tab)
        {
            _viewModel.NextTabCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl alone: AltGr (Ctrl+Alt) with a digit types a character on many layouts.
        else if (CommandKey.AsWritten(e.KeyModifiers) == KeyModifiers.Control && ViewForKey(e.Key) is { } view)
        {
            _viewModel.SetEditorModeCommand.Execute(view);
            e.Handled = true;
        }
        // Heading levels in the editor with the keyboard: Ctrl+Shift+1 to 6, and 0 for a plain line.
        else if (CommandKey.AsWritten(e.KeyModifiers) == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key is >= Key.D0 and <= Key.D6
                 && (FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<TextEditor>(includeSelf: true) is { } editor
                 && CompletionController.For(editor) is { } assist)
        {
            assist.Heading(e.Key - Key.D0);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.CommandPalette.IsOpen)
        {
            _viewModel.CommandPalette.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.SyntaxReference.IsOpen)
        {
            _viewModel.SyntaxReference.Close();
            e.Handled = true;
        }
        // The page's changes are in the source's history, also while the source is hidden.
        // Not under an open block, whose own editor has its own history.
        else if (CommandKey.AsWritten(e.KeyModifiers) is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift) && e.Key is Key.Z or Key.Y
                 && PreviewPane.IsKeyboardFocusWithin && !_viewModel.InPlaceEditOpen && !_viewModel.IsReadMode
                 && FindDescendant<SourceEditorView>(EditorPane) is { } sourceView && sourceView.DataContext == _viewModel.SelectedDocument
                 && sourceView.Editor is { } source)
        {
            if (e.Key == Key.Y || shift) source.Redo(); else source.Undo();
            e.Handled = true;
        }
        // F6 moves between the source and the page.
        else if (e.Key == Key.F6 && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            SwitchPane(Regions.FirstOrDefault(r => r.IsKeyboardFocusWithin), e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            e.Handled = true;
        }
        // The rest of the menus' keys. A menu item's InputGesture only shows its key; both
        // menu sets show the same ones, so the hamburger's set is the one looked up.
        else if ((e.KeyModifiers != KeyModifiers.None || e.Key is >= Key.F1 and <= Key.F24)
                 && HamburgerMenuItem.GetLogicalDescendants().OfType<MenuItem>().FirstOrDefault(i => i.InputGesture?.Matches(e) == true)
                     is { Command: { } command } item)
        {
            if (command.CanExecute(item.CommandParameter)) command.Execute(item.CommandParameter);
            e.Handled = true;
        }
    }

    /// <summary>A document on the page takes the keyboard when nothing that reads keys has it
    /// (nothing, the window, a clicked button or a hidden control), so its keys show and work at once.</summary>
    private void GiveThePageAnIdleKeyboard()
    {
        if (_viewModel is { EditorMode: EditorMode.Wysiwyg or EditorMode.Read, SelectedDocument: not null }
            && FocusManager?.GetFocusedElement() is null or TopLevel or Button or Visual { IsEffectivelyVisible: false })
            FindDescendant<PreviewView>(PreviewPane)?.FocusPage();
    }

    private static EditorMode? ViewForKey(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => EditorMode.Source,
        Key.D2 or Key.NumPad2 => EditorMode.Split,
        Key.D3 or Key.NumPad3 => EditorMode.Wysiwyg,
        Key.D4 or Key.NumPad4 => EditorMode.Read,
        _ => null,
    };

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
        if (editorView is null || !EditorPane.IsVisible) // WYSIWYG and Read keep it hidden: no caret to see
        {
            _viewModel.StatusText = Strings.StatusSwitchModeToInsert;
            return;
        }

        var text = Ascolotl.Core.Editing.Assist.Snippet.Parse(item.Snippet).Text;
        editorView.InsertAtCaret(text);
    }

    // --- Zoom gestures ---

    // Smooth zoom: 10 % per wheel notch, proportionally less for a touchpad's
    // fractional deltas (Windows sends a pinch as Ctrl+wheel).
    private void OnZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        _viewModel.ZoomBy(Math.Pow(1.1, e.Delta.Y));
    }

    // macOS pinch: Delta.X is the relative magnification change.
    private void OnZoomMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_viewModel is null) return;
        e.Handled = true;
        _viewModel.ZoomBy(1 + e.Delta.X);
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
            if (ext is ".adoc" or ".asciidoc" or ".txt" or ".asc" || MarkdownFile.Is(path))
            {
                await _viewModel.OpenFileByPathAsync(path);
            }
        }
    }

    // --- Tab middle-click ---

    private void OnTabStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return; // Ctrl+wheel zooms
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        TabScroll.Offset = TabScroll.Offset.WithX(TabScroll.Offset.X - delta * 60);
        e.Handled = true;
    }

    private void OnAllTabsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is not { } vm)
            return;
        var flyout = new MenuFlyout();
        foreach (var doc in vm.Documents)
        {
            var item = new MenuItem
            {
                Header = doc.TabTitle.Replace("_", "__"), // "_" marks access keys in menu headers
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = doc == vm.SelectedDocument,
            };
            ToolTip.SetTip(item, doc.FilePath ?? Strings.TabNotSavedYet);
            item.Click += (_, _) => vm.SelectedDocument = doc;
            flyout.Items.Add(item);
        }
        flyout.ShowAt(AllTabsButton);
    }

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

    private void OnOutlineItemTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel is not null && (e.Source as StyledElement)?.DataContext is OutlineItem item)
            _viewModel.NavigateToOutlineItemCommand.Execute(item);
    }

    private void OnNavigateToLine(int lineNumber)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // WYSIWYG hides the source pane, so move the page instead.
            if (_viewModel?.EditorMode is EditorMode.Wysiwyg or EditorMode.Read && FindDescendant<PreviewView>(PreviewPane) is { } page)
            {
                page.ScrollToSourceLine(lineNumber, (_viewModel.SelectedDocument?.SourceText ?? "").AsSpan().Count('\n') + 1);
                // The keyboard follows, as it does to the source: onto the section's first block.
                Dispatcher.UIThread.Post(page.FocusPage, DispatcherPriority.Background);
            }
            else
                FindDescendant<SourceEditorView>(EditorPane)?.ScrollToLine(lineNumber);
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
        Dispatcher.UIThread.Post(GiveThePageAnIdleKeyboard, DispatcherPriority.Background); // a restored session
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
            // The source stops at its last line, so over its last screen the
            // preview eases to its own end; once there it stays while the
            // source scrolls on (pulling the source back made it jump).
            var toEnd = ScrollSyncCalculator.EaseToEnd(
                sv.Offset.Y, sv.Extent.Height - sv.Viewport.Height, sv.Viewport.Height);
            _cachedPreviewView?.SyncToSourceLine(line, frac, totalLines, toEnd);
        }
        finally
        {
            ClearSyncFlagsAfterLayout();
        }
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
