using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Avalonia;
using AdocNet.Converters.Pdf;
using AdocNet.Layout;
using AdocNet.Layout.Builders;
using Ascolotl.App.Views.Assist;
using Ascolotl.App.Services;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Editing.Assist;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.ViewModels;
using Ascolotl.Export;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Ascolotl.Core.Resources;

namespace Ascolotl.App.Views;

public partial class PreviewView : UserControl
{
    private DocumentViewModel? _doc;
    private MainWindowViewModel? _mainVm;

    // Last sync position — re-applied after each content update so the preview
    // doesn't jump to the top when a debounced re-render replaces the content.
    private int _lastSyncLine;
    private double _lastSyncFrac;
    private double _lastSyncToEnd;
    private bool _restoreTopLine; // a tab just shown: back to its TopLine after its first layout
    private int _lastSyncTotal;

    // (sourceLine → Y in preview content) — built after each render by walking
    // the Layout's flat BlockLayout list in lockstep with the rendered panel's
    // children. For TableLayout blocks we additionally drill into the rendered
    // Grid and add one marker per row, so scrolling within a long table
    // tracks row-by-row. Null when not yet built; sync scroll falls back to
    // ratio-based in that case.
    private SortedList<int, double>? _lineMap;
    private Control? _renderedRoot;
    // AST-line → editor-line translation for the current document, accounting
    // for include:: expansion. Rebuilt with the line map; consulted by both
    // the line-map builder and click-to-source (inline hit-test).
    private int[] _astToEditor = Array.Empty<int>();

    // Bare renderer (WrapInScrollViewer=false) gives us the content panel
    // directly. IncrementalAvaloniaRenderer nests its output one section deep,
    // which collapses the line map to one marker per top-level AST child, so
    // re-renders reuse unchanged block visuals here instead (TryReuseBlocks).
    private static readonly AvaloniaRenderer _avaloniaRenderer = new() { WrapInScrollViewer = false };
    // The renderer's own look, restored when no style is active.
    private static readonly AvaloniaRenderTheme _builtInTheme = _avaloniaRenderer.Theme;
    private static readonly LayoutBuilder _layoutBuilder = new();

    // Page preview: shares the block renderer; A4 geometry mirrors the PDF
    // export's default page setup (595×842 pt) exactly, via points→DIPs.
    private static readonly PagedAvaloniaRenderer _pagedRenderer = new(_avaloniaRenderer);
    // Page Preview lays pages out off the UI thread; a render started later
    // supersedes one still running (its result is dropped).
    private int _renderGeneration;
    // Authoritative 1:1 map of each rendered top-level block visual to its
    // BlockLayout (whose .Source is the ABSOLUTE document source range).
    // Built by rendering block-by-block via AvaloniaRenderer.Render(BlockLayout),
    // which guarantees one visual per layout block — no positional drift.
    private List<(Control Vis, BlockLayout Block)> _blockMap = new();
    // What the shown block panel was built around; while a new render keeps it,
    // only the blocks that changed are rebuilt. Null when nothing reusable is shown.
    private PreviewFrame? _frame;
    // Lines the source moved by since a kept block visual was rendered: its
    // inlines still carry that render's absolute lines (see CurrentRange).
    private Dictionary<Control, int> _lineShift = new();
    // The panel child holding a committed in-place edit's frozen editor (the
    // editor itself, or the table around a cell editor) until the re-render,
    // which must rebuild that slot even if its block renders the same.
    private Control? _frozenEditSlot;
    // Guards a single subscription to the shared renderer's LinkClicked.
    private static bool _linkHandlerWired;

    // In-flight block-WYSIWYG edit: the rendered block visual has been
    // swapped for an in-place source editor. Null when not editing.
    // UpdatePreview never rebuilds the panel under it: the rebuild is
    // postponed (_rebuildPending) until the edit is committed or cancelled.
    private BlockEditState? _openEdit;

    // The toolbar shows Apply and Cancel while an edit is open.
    private BlockEditState? _activeEdit
    {
        get => _openEdit;
        set
        {
            _openEdit = value;
            if (_mainVm is not null) _mainVm.InPlaceEditOpen = value is not null;
            if (value is null && _doc is not null) _doc.UnappliedEdit = null;
            ShowPageKeys();
        }
    }
    private bool _rebuildPending;

    // The source text the current panel's block map was rendered from. Its
    // ranges index that text, so gestures that splice the source refuse while
    // the document has moved on and the re-render is still pending.
    private string? _panelSource;

    // Page Preview: one page layout runs at a time; requests meanwhile
    // collapse into one rerun when it finishes.
    private bool _pageLayoutRunning;
    private bool _pageLayoutQueued;

    private Window? _window;

    // When a single click both commits the open edit AND lands on another
    // block, we queue that block here so the click opens it too:
    //   EditorStart — the block's (or, for a table cell, the table's) editor
    //                 start offset, used to re-find the block after re-render.
    //   RowIndex/CellIndex — the clicked cell, or -1/-1 for a whole block.
    //   AtEnd — the caret goes to the block's end (an Up arrow came from below).
    // If the commit changed the source it's consumed after the debounced
    // re-render rebuilds the block map; if the commit was a no-op it's opened
    // immediately against the live map.
    private (int EditorStart, int RowIndex, int CellIndex, bool AtEnd)? _pendingReentry;

    /// <summary>When set, the next block edit selects all its text so the first
    /// keystroke replaces it (used for the fresh "New paragraph." placeholder).</summary>
    private bool _selectAllOnNextEdit;

    /// <summary>Where the caret goes, in the source, when the queued block opens: two blocks joined, between them.</summary>
    private int? _caretOnNextEdit;

    /// <summary>A new paragraph about to open: the text before it, the text with it, where it
    /// starts, and whether the document had changes. Cancelling it goes back.</summary>
    private (string Before, string After, int At, bool WasDirty)? _newParagraph;
    // Fit Width: margin each side of the scaled page, the pane width the
    // current render was scaled for, and the resize debounce.
    private const double FitWidthGap = 12;
    private double _renderedViewportWidth;
    private readonly DispatcherTimer _fitWidthTimer;
    // Zoom gestures rescale live; the full re-render (sharp layout, line map,
    // scroll position) waits until the gesture pauses.
    private readonly DispatcherTimer _zoomSettleTimer;

    public PreviewView()
    {
        InitializeComponent();
        PreviewHost.Content = _pages;
        _pages.Children.Add(new Canvas { IsHitTestVisible = false, ZIndex = 1, Children = { _findLayer, _selectionBox, _outline } });
        InitFind();
        _outline.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ForgeAccentBrush"));
        _selectionBox.Bind(Border.BackgroundProperty, this.GetResourceObservable("ForgeAccentBrush"));
        PreviewScroll.LayoutUpdated += (_, _) => PlaceOutline();
        DataContextChanged += OnDataContextChanged;

        // Hyperlink clicks (1.0.9): suppress the renderer's default
        // OS-shell navigation while editing — a stray click on a link in
        // the preview shouldn't yank the user into a browser. The
        // click-to-source handler still runs and moves the editor caret to
        // the link's source. Wired once: the renderer is shared/static, so
        // multiple PreviewView instances must not each add a handler.
        if (!_linkHandlerWired)
        {
            _linkHandlerWired = true;
            _avaloniaRenderer.LinkClicked += (_, args) => args.Handled = true;
        }

        if (Application.Current is not null)
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;

        // Reverse sync: when the user scrolls the preview, ask the host to
        // scroll the editor to the matching source line. The host (MainWindow)
        // owns the editor's ScrollViewer; we just publish a request.
        PreviewScroll.ScrollChanged += OnPreviewScrollChanged;

        // Fit Width follows the pane: re-render once a resize settles (the
        // line map must be rebuilt at the new scale, so no live transform tweak).
        _zoomSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _zoomSettleTimer.Tick += (_, _) =>
        {
            _zoomSettleTimer.Stop();
            if (_doc is not null)
                UpdatePreview(_doc);
        };

        _fitWidthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _fitWidthTimer.Tick += (_, _) =>
        {
            _fitWidthTimer.Stop();
            if (_doc is not null && _mainVm?.PreviewFitWidth == true
                && Math.Abs(PreviewScroll.Viewport.Width - _renderedViewportWidth) > 1)
                UpdatePreview(_doc);
        };
        PreviewScroll.SizeChanged += (_, _) =>
        {
            if (_mainVm?.PreviewFitWidth == true)
            {
                _fitWidthTimer.Stop();
                _fitWidthTimer.Start();
            }
        };

        // Click-to-source + block-WYSIWYG gestures: pointer presses bubble up
        // through the rendered visual tree. handledEventsToo so clicks on
        // inner Borders/TextBlocks (which mark the event handled) still reach
        // us. Bubble only — registering for both Tunnel and Bubble fires the
        // handler twice per click. Attached to the ScrollViewer (not the
        // content host) so clicks in the empty area below a short document
        // also reach us — that's what lets "click anywhere outside" commit an
        // open block edit.
        PreviewScroll.AddHandler(InputElement.PointerPressedEvent,
            OnPreviewPointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // Without a mouse: one block at a time takes the keyboard. The arrows move
        // between blocks, Enter edits one, Shift+F10 or the Menu key opens its menu.
        // Handled keys too: the scroll viewer takes Page Up/Down first.
        PreviewScroll.AddHandler(InputElement.KeyDownEvent, OnPageKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        // Shift+F10's menu opens as F10 is let go: let go in the menu, F10 would close it at once (it toggles menus).
        PreviewScroll.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (e.Key != Key.F10 || !_menuOnF10Up) return;
            _menuOnF10Up = false;
            OpenBlockMenu();
            e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        // Tab or F6 gives the page the keyboard: it goes on to a block. A block or
        // editor removed by a re-render drops it: it comes back once the new blocks are in.
        PreviewScroll.GotFocus += (_, e) =>
        {
            PlaceOutline();
            if (e.Source != PreviewScroll || e.NavigationMethod == NavigationMethod.Pointer) return;
            if (e.NavigationMethod == NavigationMethod.Tab) FocusBlock(CurrentInView());
            else RefocusSoon();
        };
        PreviewScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsKeyboardFocusWithinProperty) return;
            ShowPageKeys();
            PlaceOutline();
            if (e.NewValue is false) RefocusSoon(); // it checks, once settled, that the keyboard went nowhere
        };
    }

    // The block the keyboard is on, or returns to after an edit: an index into _blockMap.
    private int _current;

    /// <summary>The status bar lists the page's keys while it has the keyboard and no block is open.</summary>
    private void ShowPageKeys()
    {
        if (_mainVm is not null) _mainVm.PageHasKeyboard = PreviewScroll.IsKeyboardFocusWithin && _openEdit is null;
    }

    private IInputElement? FocusedElement => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

    /// <summary>Puts the keyboard back on a block once the page's blocks have settled.</summary>
    private void RefocusSoon() => Dispatcher.UIThread.Post(() =>
    {
        if (_activeEdit is null && FocusedElement is var f && (f is null || f == PreviewScroll || f is Visual { IsEffectivelyVisible: false }))
            FocusPage();
    }, DispatcherPriority.Background);

    /// <summary>Gives the page the keyboard, e.g. once the source it was in is hidden.</summary>
    public void FocusPage()
    {
        if (!FocusBlock(CurrentInView()))
            PreviewScroll.Focus(NavigationMethod.Tab);
    }

    private bool InView(Control vis) =>
        vis.TranslatePoint(default, PreviewScroll) is { } top && top.Y < PreviewScroll.Viewport.Height
        && vis.TranslatePoint(new Point(0, vis.Bounds.Height), PreviewScroll) is { Y: > 0 };

    /// <summary>The current block, or the first one in view once it has scrolled out of sight; -1 when none is.</summary>
    private int CurrentInView() =>
        _current < _blockMap.Count && InView(_blockMap[_current].Vis) ? _current : _blockMap.FindIndex(b => InView(b.Vis));

    /// <summary>Puts the keyboard on block <paramref name="i"/>, named for screen readers. False when it can't take it.</summary>
    private bool FocusBlock(int i, bool bringIntoView = true)
    {
        if (i < 0 || i >= _blockMap.Count || _mainVm?.PagePreview == true) return false;
        var (vis, block) = _blockMap[i];
        _current = i;
        vis.Focusable = true;
        vis.IsTabStop = false; // the page is one Tab stop
        vis.FocusAdorner = null; // see PlaceOutline
        vis.Classes.Add(FocusBlockClass);
        Describe(vis, block);
        if (!vis.Focus(NavigationMethod.Directional)) return false;
        if (bringIntoView) vis.BringIntoView();
        return true;
    }

    private const string FocusBlockClass = "focus-block";

    // The block the keyboard is on is outlined clear of its text. The outline is drawn in the
    // page, under the scroll viewer's clip: a focus adorner is clipped to the block itself.
    private readonly Border _outline = new() { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), IsVisible = false };

    private void PlaceOutline()
    {
        if (PreviewScroll.IsKeyboardFocusWithin && FocusedElement is Control vis && vis.Classes.Contains(FocusBlockClass)
            && vis.TranslatePoint(default, _pages) is { } tl
            && vis.TranslatePoint(new Point(vis.Bounds.Width, vis.Bounds.Height), _pages) is { } br)
        {
            Canvas.SetLeft(_outline, tl.X - 7);
            Canvas.SetTop(_outline, tl.Y - 4);
            _outline.Width = br.X - tl.X + 14;
            _outline.Height = br.Y - tl.Y + 8;
            _outline.IsVisible = true;
        }
        else
            _outline.IsVisible = false;

        // Visibility set once: hiding then showing it again would lay the page out again, and again.
        var selected = false;
        if (Selection is var (first, last) && PreviewScroll.IsKeyboardFocusWithin)
        {
            var (left, top, right, bottom) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            for (var i = first; i <= last; i++)
                if (_blockMap[i].Vis is var v && v.TranslatePoint(default, _pages) is { } a
                    && v.TranslatePoint(new Point(v.Bounds.Width, v.Bounds.Height), _pages) is { } b)
                    (left, top, right, bottom) = (Math.Min(left, a.X), Math.Min(top, a.Y), Math.Max(right, b.X), Math.Max(bottom, b.Y));
            if (right > left)
            {
                Canvas.SetLeft(_selectionBox, left - 7);
                Canvas.SetTop(_selectionBox, top - 4);
                _selectionBox.Width = right - left + 14;
                _selectionBox.Height = bottom - top + 8;
                selected = true;
            }
        }
        _selectionBox.IsVisible = selected;
        if (FindBar.IsVisible) PlaceFindMarks();
    }

    // ── Find on the page ──────────────────────────────────────────────────
    // ponytail: the rendered text block by block: a match across two styles or blocks isn't found.

    private readonly Canvas _findLayer = new();
    private List<(TextBlock Text, int Start, int Length)> _findMatches = [];
    private int _findIndex = -1;
    private (Rect, Rect, List<(TextBlock, int, int)>, int) _findPlaced;

    private void InitFind()
    {
        FindBox.Watermark = Strings.FindOnPage;
        AutomationProperties.SetName(FindBox, Strings.FindOnPage);
        foreach (var (button, name) in new[] { (FindPreviousButton, Strings.FindPrevious), (FindNextButton, Strings.FindNext), (FindCloseButton, Strings.FindClose) })
        {
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);
        }
        FindBox.TextChanged += (_, _) => FindMatches(keepPlace: false);
        FindPreviousButton.Click += (_, _) => FindStep(-1);
        FindNextButton.Click += (_, _) => FindStep(1);
        FindCloseButton.Click += (_, _) => CloseFind();
        FindBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) CloseFind();
            else if (e.Key is Key.Enter or Key.F3) FindStep(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        // Ctrl+F anywhere on the page, an open block's editor included; F3 on to the next match.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) OpenFind();
            else if (e.Key == Key.F3 && FindBar.IsVisible) FindStep(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    private void OpenFind()
    {
        if (_activeEdit is not null) CommitBlockEdit(); // an open block shows its source, not its text
        FindBar.IsVisible = true;
        FindBox.Focus();
        FindBox.SelectAll();
        FindMatches(keepPlace: false);
    }

    /// <summary>Closes the bar; the keyboard goes to the block of the match last shown.</summary>
    private void CloseFind()
    {
        var shown = _findIndex >= 0 && _findIndex < _findMatches.Count ? _findMatches[_findIndex].Text : null;
        FindBar.IsVisible = false;
        _findMatches = [];
        _findIndex = -1;
        PlaceFindMarks();
        var block = shown is null ? -1 : _blockMap.FindIndex(b => shown.GetSelfAndVisualAncestors().Contains(b.Vis));
        if (!FocusBlock(block >= 0 ? block : CurrentInView())) PreviewScroll.Focus();
    }

    /// <summary>Every place the find text occurs in the rendered text, case aside. The current one:
    /// the first in view, or after a re-render the same number as before.</summary>
    private void FindMatches(bool keepPlace)
    {
        var query = FindBar.IsVisible ? FindBox.Text ?? "" : "";
        var matches = new List<(TextBlock, int, int)>();
        if (query.Length > 0 && _renderedRoot is not null)
            foreach (var text in _renderedRoot.GetVisualDescendants().OfType<TextBlock>())
            {
                var shown = text.Inlines is { Count: > 0 } inlines ? inlines.Text : text.Text;
                for (var i = shown?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1; i >= 0;
                     i = shown!.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase))
                    matches.Add((text, i, query.Length));
            }
        _findMatches = matches;
        _findIndex = matches.Count == 0 ? -1
            : keepPlace ? Math.Clamp(_findIndex, 0, matches.Count - 1)
            : Math.Max(0, matches.FindIndex(m => m.Item1.TranslatePoint(default, PreviewScroll) is { Y: >= 0 }));
        ShowFindCount(query);
        PlaceFindMarks();
        if (!keepPlace) BringMatchIntoView();
    }

    private void FindStep(int step)
    {
        if (_findMatches.Count == 0) return;
        _findIndex = (_findIndex + step + _findMatches.Count) % _findMatches.Count;
        ShowFindCount(FindBox.Text ?? "");
        PlaceFindMarks();
        BringMatchIntoView();
    }

    private void ShowFindCount(string query)
    {
        FindCount.Text = _findMatches.Count > 0 ? string.Format(Strings.FindCount, _findIndex + 1, _findMatches.Count)
            : query.Length > 0 ? Strings.FindNoMatch : "";
        if (_mainVm is not null && FindCount.Text.Length > 0) _mainVm.StatusText = FindCount.Text; // read out too
    }

    private void BringMatchIntoView()
    {
        if (_findIndex < 0 || _findIndex >= _findMatches.Count) return;
        var (text, start, length) = _findMatches[_findIndex];
        if (text.TextLayout.HitTestTextRange(start, length).FirstOrDefault() is { Width: > 0 } r)
            text.BringIntoView(r.Translate(new Vector(text.Padding.Left, text.Padding.Top)).Inflate(24));
    }

    /// <summary>The matches marked over the text, the current one stronger. Marks are reused and only
    /// moved: adding or dropping one each layout pass would lay the page out again without end.</summary>
    private void PlaceFindMarks()
    {
        // Each layout pass calls this, each scroll step too: the marks move only with the page's size or the matches.
        var placed = (_pages.Bounds, _renderedRoot?.Bounds ?? default, _findMatches, _findIndex); // a zoom: the pages' size
        if (placed == _findPlaced) return;
        _findPlaced = placed;
        var marks = new List<(Rect Rect, bool Current)>();
        for (var m = 0; m < _findMatches.Count; m++)
        {
            var (text, start, length) = _findMatches[m];
            if (!text.IsEffectivelyVisible || text.TranslatePoint(new Point(text.Padding.Left, text.Padding.Top), _pages) is not { } origin) continue;
            foreach (var r in text.TextLayout.HitTestTextRange(start, length))
                marks.Add((r.Translate(new Vector(origin.X, origin.Y)), m == _findIndex));
        }
        while (_findLayer.Children.Count > marks.Count) _findLayer.Children.RemoveAt(_findLayer.Children.Count - 1);
        while (_findLayer.Children.Count < marks.Count) _findLayer.Children.Add(new Border { CornerRadius = new CornerRadius(2) });
        this.TryFindResource("ForgeAccentBrush", ActualThemeVariant, out var accent);
        for (var k = 0; k < marks.Count; k++)
        {
            var (r, isCurrent) = marks[k];
            var mark = (Border)_findLayer.Children[k];
            Canvas.SetLeft(mark, r.X - 1);
            Canvas.SetTop(mark, r.Y);
            mark.Width = r.Width + 2;
            mark.Height = r.Height;
            mark.Background = accent as IBrush;
            mark.Opacity = isCurrent ? 0.45 : 0.18; // every match seen, the current one stronger
        }
    }

    // Blocks selected with Shift+↑/↓ or Ctrl+A run from _selectAnchor to _current; -1: none.
    private int _selectAnchor = -1;
    private readonly Border _selectionBox = new() { CornerRadius = new CornerRadius(4), Opacity = 0.15, IsVisible = false };

    private (int First, int Last)? Selection => _selectAnchor >= 0 && _selectAnchor < _blockMap.Count && _current < _blockMap.Count
        ? (Math.Min(_selectAnchor, _current), Math.Max(_selectAnchor, _current)) : null;

    private void Select(int anchor)
    {
        _selectAnchor = anchor;
        PlaceOutline();
        if (Selection is var (first, last) && last > first && _mainVm is not null) // one: the block itself is announced
            _mainVm.StatusText = string.Format(Strings.StatusBlocksSelected, last - first + 1);
    }

    private void ClearSelection()
    {
        if (_selectAnchor < 0) return;
        _selectAnchor = -1;
        PlaceOutline();
    }

    /// <summary>The source of blocks <paramref name="first"/> to <paramref name="last"/>, with their title
    /// and attribute lines; null when one of the two comes from an included file.</summary>
    private (int Start, int Length)? Blocks(int first, int last) =>
        WholeBlock(first) is { } f && WholeBlock(last) is { } l && l.Start + l.Length >= f.Start
            ? (f.Start, l.Start + l.Length - f.Start) : null;

    private void MakeCurrent(Control vis)
    {
        if (_blockMap.FindIndex(b => b.Vis == vis) is var i and >= 0) _current = i;
    }

    private static void Describe(Control vis, BlockLayout block)
    {
        AutomationProperties.SetName(vis, block is ImageLayout image ? image.Alt ?? string.Empty
            : string.Join(" ", vis.GetSelfAndVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Inlines is { Count: > 0 } inlines ? inlines.Text : t.Text)
                .Where(s => !string.IsNullOrWhiteSpace(s))));
        AutomationProperties.SetControlTypeOverride(vis, block is ImageLayout ? AutomationControlType.Image : AutomationControlType.Text);
        // A panel's peer is hidden from screen readers unless it says it is content.
        AutomationProperties.SetAccessibilityView(vis, AccessibilityView.Content);
        // AdocNet counts sections from 1 (==); for a reader the document title (=) is heading 1.
        if (block is HeadingLayout heading) AutomationProperties.SetHeadingLevel(vis, heading.Level + 1);
    }

    private bool _menuOnF10Up;

    /// <summary>The menu of the block the keyboard is on (or the insert menu), at the block: no pointer opened it.</summary>
    private void OpenBlockMenu()
    {
        if (_mainVm?.PagePreview == true) return;
        if (CurrentInView() is var i and >= 0)
            ShowBlockContextMenu(_blockMap[i].Vis, _blockMap[i].Block, null, _blockMap[i].Vis.TranslatePoint(new Point(1, 1), PreviewScroll) ?? default);
        else
            ShowInsertMenuAt(default);
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        // The page or one of its blocks: keys typed in the in-place editor are its own.
        var on = _blockMap.FindIndex(b => b.Vis == e.Source);
        if (e.Source != PreviewScroll && on < 0) return;
        var mods = e.KeyModifiers;
        // Avalonia opens a menu on these keys only for an attached ContextMenu; the page builds one per block.
        if (e.Key == Key.Apps && mods == KeyModifiers.None || e.Key == Key.F10 && mods == KeyModifiers.Shift)
        {
            if (e.Key == Key.F10) _menuOnF10Up = true; // opened as F10 is let go
            else OpenBlockMenu();
            e.Handled = true;
            return;
        }
        if (e.Handled)
        {
            // The scroll viewer paged: the keyboard follows to a block still in view.
            if (e.Key is Key.PageUp or Key.PageDown && on >= 0)
                Dispatcher.UIThread.Post(() => { if (_activeEdit is null) FocusBlock(CurrentInView(), bringIntoView: false); },
                    DispatcherPriority.Background);
            return;
        }
        // Back out of the page: going back through the page itself would hand the keyboard to a block again.
        if (e.Key == Key.Tab && mods == KeyModifiers.Shift && on >= 0)
        {
            e.Handled = KeyboardNavigationHandler.GetNext(PreviewScroll, NavigationDirection.Previous)?.Focus(NavigationMethod.Tab) == true;
            return;
        }
        // Several blocks: Shift+↑/↓ widens the selection from the block the keyboard was on, Ctrl+A takes them all.
        if (on >= 0 && mods == KeyModifiers.Shift && e.Key is Key.Up or Key.Down)
        {
            if (_selectAnchor < 0) _selectAnchor = on;
            FocusBlock(Math.Clamp(on + (e.Key == Key.Up ? -1 : 1), 0, _blockMap.Count - 1));
            Select(_selectAnchor);
            e.Handled = true;
            return;
        }
        if (on >= 0 && mods == KeyModifiers.Control && e.Key == Key.A)
        {
            FocusBlock(_blockMap.Count - 1, bringIntoView: false);
            Select(0);
            e.Handled = true;
            return;
        }
        if (_selectAnchor >= 0 && e.Key == Key.Escape)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }
        // Any other key ends the selection, but a modifier alone and those acting on it.
        if (e.Key is not (Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt)
            && (e.Key, mods) is not ((Key.Up or Key.Down, KeyModifiers.Control | KeyModifiers.Shift) or (Key.D, KeyModifiers.Control | KeyModifiers.Alt)
                or (Key.Delete, KeyModifiers.None) or (Key.C or Key.X, KeyModifiers.Control)))
            ClearSelection();
        if (e.Key is Key.Home or Key.End && mods is KeyModifiers.None or KeyModifiers.Control)
        {
            if (e.Key == Key.Home) PreviewScroll.ScrollToHome(); else PreviewScroll.ScrollToEnd();
            FocusBlock(e.Key == Key.Home ? 0 : _blockMap.Count - 1, bringIntoView: false);
            e.Handled = true;
            return;
        }
        // The block itself: moved, duplicated, deleted, or a new paragraph after it. Ctrl+Z undoes it.
        if (on >= 0 && (e.Key, mods) switch
            {
                (Key.Up or Key.Down, KeyModifiers.Control | KeyModifiers.Shift) => MoveBlock(on, e.Key == Key.Up ? -1 : 1),
                (Key.D, KeyModifiers.Control | KeyModifiers.Alt) => DuplicateBlock(on),
                (Key.Delete, KeyModifiers.None) => DeleteBlock(on),
                (Key.C or Key.X, KeyModifiers.Control) => CopyBlocks(on, cut: e.Key == Key.X),
                (Key.V, KeyModifiers.Control) => PasteBlocks(on),
                (Key.N, KeyModifiers.Control | KeyModifiers.Shift) => NewParagraphAfter(on),
                (>= Key.D0 and <= Key.D6, KeyModifiers.Control | KeyModifiers.Shift) =>
                    TurnBlockInto(on, e.Key == Key.D0 ? BlockKind.Paragraph : BlockKind.Heading1 + (e.Key - Key.D1)),
                _ when IsTurnIntoKey(e) => ShowTurnIntoMenu(on),
                _ => (bool?)null,
            } is { } done)
        {
            _refocusCurrent = done;
            e.Handled = true;
            return;
        }
        if (mods != KeyModifiers.None) return;
        if (e.Key is Key.Up or Key.Down)
        {
            var step = e.Key == Key.Up ? -1 : 1;
            if (_blockMap.Count == 0) // Page Preview: no blocks, the arrows scroll
            {
                if (step < 0) PreviewScroll.LineUp(); else PreviewScroll.LineDown();
            }
            else if (on < 0 || !InView(_blockMap[on].Vis))
                FocusBlock(CurrentInView());
            else if (on + step >= 0 && on + step < _blockMap.Count)
                FocusBlock(on + step);
            else if (step < 0) PreviewScroll.ScrollToHome(); // up to the title
            // Below the last block: the rest of it first, then a new paragraph.
            else if (_doc is null || _blockMap[on].Vis.TranslatePoint(new Point(0, _blockMap[on].Vis.Bounds.Height), PreviewScroll)
                     is { } bottom && bottom.Y > PreviewScroll.Viewport.Height + 1)
                PreviewScroll.ScrollToEnd();
            else OpenNewParagraph(_doc.SourceText.Length);
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Enter or Key.F2) || _mainVm?.PagePreview == true) return;
        var hit = CurrentInView();
        if (hit < 0) return;
        var (vis, block) = _blockMap[hit];
        e.Handled = block is TableLayout table
            ? EnterCellEditByIndex(vis, table, 0, 0)
            : EnterBlockEdit(vis, block);
    }

    /// <summary>
    /// Raised when the user scrolled the preview manually — the host should
    /// scroll the editor so its first visible line matches. Continuous
    /// "follow the preview" semantics.
    /// </summary>
    public event Action<int, double>? SourceLineRequested;

    /// <summary>
    /// Raised when the user clicked on a rendered element in the preview.
    /// Carries (editorLine, column, clickViewportY), all resolved against the
    /// editor's own source: <c>editorLine</c> and <c>column</c> are 0-based
    /// editor coordinates (column is 0 when only line-level granularity was
    /// available), and <c>clickViewportY</c> is where the click landed in the
    /// preview viewport so the host can align that source line to the same
    /// on-screen row. When 1.0.9 inline source ranges resolve the click, the
    /// line+column are exact (the clicked word); otherwise they come from the
    /// block/table-row line map.
    /// </summary>
    public event Action<int, int, double>? SourceLineClicked;

    /// <summary>
    /// Suppress one round of SourceLineRequested events. Set by the host
    /// while it's syncing the preview from the editor, to prevent the
    /// preview's resulting ScrollChanged from bouncing right back.
    /// </summary>
    public bool SuppressReverseSync { get; set; }

    /// <summary>For a freshly created (empty) document edited in WYSIWYG mode:
    /// insert a placeholder paragraph and open it for editing (text selected, so
    /// the first keystroke replaces it). No-op if the document already has content.</summary>
    public void StartFreshParagraphEdit()
    {
        if (_doc is null || !string.IsNullOrWhiteSpace(_doc.SourceText)) return;

        // Queue the re-entry first, then make the change: the debounced re-render
        // rebuilds the block map and ConsumeReentry opens the new paragraph.
        _selectAllOnNextEdit = true;
        _pendingReentry = (0, -1, -1, false);
        _doc.SourceText = BlockSnippets.Paragraph;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is not null)
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        if (_mainVm is not null)
        {
            _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
            _mainVm.FlushPendingEdits = null;
            _mainVm.CancelPendingEdits = null;
            _mainVm.InPlaceEditOpen = false;
            _mainVm.PageHasKeyboard = false;
        }
        _window = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        _mainVm = _window?.DataContext as MainWindowViewModel;
        if (_mainVm is not null)
        {
            _mainVm.PropertyChanged += OnMainVmPropertyChanged;
            // Saving and closing must see the open in-place edit's text.
            _mainVm.FlushPendingEdits = CommitBlockEdit;
            _mainVm.CancelPendingEdits = CancelBlockEdit;
        }
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.PreviewWordWrap)
                or nameof(MainWindowViewModel.PagePreview)
                or nameof(MainWindowViewModel.StylePath)
                or nameof(MainWindowViewModel.PreviewFitWidth)
            && _doc is not null)
        {
            UpdatePreview(_doc);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Re-render preview so the AdocNet.Avalonia control picks up the new
        // ActualThemeVariant from its parent.
        if (_doc is not null)
            UpdatePreview(_doc);
    }

    private static bool IsDarkTheme =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // An open in-place edit belongs to the outgoing document: commit it
        // there before switching, rather than dropping the typed text.
        // (Cleared first: neither a re-entry nor a rebuild applies to it.)
        _pendingReentry = null;
        _rebuildPending = false;
        CommitBlockEdit();

        if (_doc is not null)
        {
            _doc.PropertyChanged -= OnDocPropertyChanged;
            KeepShownPage(_doc);
        }

        _doc = DataContext as DocumentViewModel;

        if (_doc is not null)
        {
            _doc.PropertyChanged += OnDocPropertyChanged;
            _restoreTopLine = true;
            UpdatePreview(_doc);
        }
    }

    private void OnDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_doc is null) return;

        if (e.PropertyName is nameof(DocumentViewModel.RenderedDocument)
            or nameof(DocumentViewModel.RenderError)
            or nameof(DocumentViewModel.LastRenderSucceeded))
        {
            UpdatePreview(_doc, reuseBlocks: true);
        }
        else if (e.PropertyName == nameof(DocumentViewModel.ZoomLevel))
        {
            if (Shown is LayoutTransformControl host && _mainVm?.PreviewFitWidth != true)
            {
                var zoom = _doc.ZoomLevel / 100.0;
                host.LayoutTransform = new ScaleTransform(zoom, zoom);
            }
            _zoomSettleTimer.Stop();
            _zoomSettleTimer.Start();
        }
    }

    /// <param name="reuseBlocks">A new render of the shown document: keep the
    /// visuals of blocks that did not change. Other callers changed how the
    /// preview looks, so they rebuild it all.</param>
    private void UpdatePreview(DocumentViewModel doc, bool reuseBlocks = false)
    {
        // Every call supersedes a page layout still running, early returns included.
        var generation = ++_renderGeneration;

        // Rebuilding would discard the open in-place edit and its typed text;
        // rebuild once it is committed or cancelled instead.
        if (_activeEdit is not null)
        {
            _rebuildPending = true;
            return;
        }
        _rebuildPending = false;
        _panelSource = null;
        var shownFrame = _frame;
        _frame = null; // set again only once a block panel is shown

        if (!doc.LastRenderSucceeded && doc.RenderError is not null)
        {
            Shown = null;
            StatusOverlay.IsVisible = true;
            StatusText.Text = doc.RenderError;
            return;
        }

        StatusOverlay.IsVisible = false;

        if (doc.RenderedDocument is null)
        {
            Shown = null;
            return;
        }

        try
        {
            // Build the layout, then render it BLOCK-BY-BLOCK via 1.0.9's
            // Render(BlockLayout) entry point. Each call returns exactly one
            // visual, so we get an authoritative 1:1 mapping from visual to
            // BlockLayout (whose .Source is the absolute document range). This
            // replaces the whole-document Render(layout) + positional pairing,
            // which drifted whenever a block emitted ≠1 visual and produced a
            // non-monotonic line map.
            var layout = _layoutBuilder.Build(doc.RenderedDocument);

            // The active style re-themes the renderer (shared, so set it on
            // every render: another document may carry its own :pdf-theme:).
            var docDir = doc.BaseDirectory ?? Directory.GetCurrentDirectory();
            var style = PreviewStyle.Load(_mainVm?.StylePath, doc.RenderedDocument.Attributes, docDir);
            _avaloniaRenderer.Theme = style is null ? _builtInTheme : PreviewStyle.ThemeFor(style);
            // Relative images load from the document's folder.
            _avaloniaRenderer.BaseDirectory = doc.BaseDirectory;
            // Print-like page preview: flow the blocks into fixed A4 pages
            // (mirroring the PDF export's default geometry) via AdocNet
            // 1.0.22's PagedAvaloniaRenderer. This is a *reading* mode — the
            // block map and line map aren't built, so WYSIWYG gestures are
            // inert (guarded at the pointer handler) and scroll-sync degrades
            // to its ratio fallback. The title always shows, matching what
            // the exported PDF prints.
            if (_mainVm?.PagePreview == true)
            {
                RenderPagePreview(doc, layout, style ?? new PdfRenderOptions(), generation);
                return;
            }

            var frame = new PreviewFrame(doc, doc.BaseDirectory, _avaloniaRenderer.Theme, style, _mainVm?.ShowTitleAndToc ?? true,
                HeaderKey(doc.RenderedDocument),
                _mainVm?.PreviewFitWidth == true ? Math.Round(PreviewScroll.Viewport.Width) : 0);
            if (reuseBlocks && frame == shownFrame && TryReuseBlocks(layout))
            {
                _frame = frame;
                _panelSource = doc.RenderedSource;
                MapAfterNextLayout();
                return;
            }
            if (!reuseBlocks && TakeKeptPage(doc, frame))
            {
                MapAfterNextLayout(); // rebuilds the line map and include offsets from the document
                return;
            }

            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var blockMap = new List<(Control, BlockLayout)>();

            // The document title (= Title) isn't a layout child — it's
            // DocumentLayout.Title. Render it ourselves so block-by-block
            // output matches the whole-document render's leading title.
            // Gated on the "Show Title & TOC" toggle: AdocNet always populates
            // DocumentLayout.Title (it doesn't honour :notitle:), so we skip
            // emitting it here when the toggle is off. (The TOC itself is
            // dropped upstream by omitting the toc parse attribute.)
            var showTitleAndToc = _mainVm?.ShowTitleAndToc ?? true;
            // A styled document that asks for a title page gets the theme's
            // title sheet above the body, as the PDF prints it; otherwise the
            // title is one heading line, like the PDF without a title page.
            var titlePage = showTitleAndToc && style is not null && TitlePageLayout.Wanted(doc.RenderedDocument)
                ? PreviewTitlePage.Build(style, doc.RenderedDocument)
                : null;
            if (titlePage is null && showTitleAndToc && !string.IsNullOrWhiteSpace(layout.Title))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = TitlePageLayout.PlainTitle(layout.Title),
                    FontWeight = style is null ? FontWeight.Bold : FontWeight.Normal,
                    FontSize = _avaloniaRenderer.Theme.DocumentTitleFontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, style is null ? 12 : style.TitleMarginBottom * PreviewStyle.DipPerPoint),
                    Foreground = _avaloniaRenderer.Theme.HeadingForeground(0),
                    FontFamily = _avaloniaRenderer.Theme.HeadingFont ?? FontFamily.Default,
                });
            }

            foreach (var block in layout.Children)
            {
                var vis = _avaloniaRenderer.Render(block);
                if (vis is null) continue;
                panel.Children.Add(vis);
                blockMap.Add((vis, block));
            }
            _blockMap = blockMap;
            _lineShift = new();
            _frozenEditSlot = null;
            _panelSource = doc.RenderedSource;

            // Bottom-spacer so the last block doesn't sit flush against the
            // viewport edge at maximum scroll. Grows the panel's DesiredSize
            // directly, which (unlike ScrollViewer padding) counts toward the
            // scroll extent — so the last line stays reachable.
            panel.Children.Add(new Border { Height = 32 });

            // Reading-grade column: cap the content width and centre it so the
            // preview reads like a page rather than full-bleed prose. The block
            // visuals are still measured relative to PreviewScroll (via
            // TranslatePoint), so scroll-sync / line-map and the bottom-fit
            // (which runs on the StackPanel via _renderedRoot) are unaffected.
            var content = new Border
            {
                MaxWidth = 860,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = panel,
            };
            if (style is not null)
            {
                // Show the styled document as a page: the theme's text width
                // (so lines break as in the PDF), on paper whatever the app theme.
                var dip = PreviewStyle.DipPerPoint;
                content.MaxWidth = style.PageWidth * dip;
                content.Padding = new Thickness(style.MarginLeft * dip, 24, style.MarginRight * dip, 0);
                content.Background = Brushes.White;
                PageEdge(content);
                TextElement.SetForeground(content, Brushes.Black);
                TextElement.SetFontSize(content, style.FontSize * dip);
            }
            // The title sheet is its own page, like the PDF's page 1.
            Shown = Zoomed(
                titlePage is null ? content
                    : new StackPanel { Spacing = 16, Children = { PageEdge(new Border { Child = titlePage }), content } },
                content.MaxWidth, doc);
            _renderedRoot = panel;

            InvalidateMeasureAll(content);
            content.UpdateLayout();
            _frame = frame;

            MapAfterNextLayout();
        }
        catch (Exception ex)
        {
            Shown = null;
            StatusOverlay.IsVisible = true;
            StatusText.Text = string.Format(Strings.StatusPreviewRenderFailed, ex.Message);
        }
    }

    // Recently shown tabs keep their built page, so switching back skips rebuilding it:
    // seconds for a long document. The kept pages stay in the tree, hidden beside the
    // shown one: attaching a page again re-measures all its text, showing it doesn't.
    // ponytail: up to three pages of controls are held; building only the visible
    // blocks (virtualizing) is the real fix.
    private readonly List<KeptPage> _keptPages = [];
    private readonly Grid _pages = new();
    private Control? _shown;

    /// <summary>The page in view; any other page in <see cref="_pages"/> is a kept one, hidden.</summary>
    private Control? Shown
    {
        get => _shown;
        set
        {
            if (_shown is not null && _shown != value)
            {
                if (_keptPages.Exists(p => p.Content == _shown)) _shown.IsVisible = false;
                else _pages.Children.Remove(_shown);
            }
            _shown = value;
            if (value is null) return;
            if (!_pages.Children.Contains(value)) _pages.Children.Add(value);
            value.IsVisible = true;
        }
    }

    private void Forget(KeptPage page)
    {
        _keptPages.Remove(page);
        if (page.Content != _shown) _pages.Children.Remove(page.Content);
    }

    private sealed record KeptPage(DocumentViewModel Doc, Control Content, Control? Root,
        List<(Control Vis, BlockLayout Block)> BlockMap, Dictionary<Control, int> LineShift, PreviewFrame Frame, string? Source);

    /// <summary>Holds on to the page shown for <paramref name="doc"/>, which is being switched away from.</summary>
    private void KeepShownPage(DocumentViewModel doc)
    {
        foreach (var stale in _keptPages.FindAll(p => p.Doc == doc || _mainVm?.Documents.Contains(p.Doc) == false))
            Forget(stale);
        // A block panel only (not Page Preview), and not one waiting for an applied edit's re-render.
        if (_shown is null || _frame is null || _panelSource is null || _frozenEditSlot is not null) return;
        _keptPages.Add(new(doc, _shown, _renderedRoot, _blockMap, _lineShift, _frame, _panelSource));
        if (_keptPages.Count > 3) Forget(_keptPages[0]);
    }

    /// <summary>Shows the page kept for <paramref name="doc"/> when it still matches what would be built.</summary>
    private bool TakeKeptPage(DocumentViewModel doc, PreviewFrame frame)
    {
        if (_keptPages.Find(p => p.Doc == doc) is not { } page) return false;
        if (page.Frame != frame || page.Source != doc.RenderedSource)
        {
            Forget(page);
            return false;
        }
        _keptPages.Remove(page);
        Shown = page.Content;
        _renderedRoot = page.Root;
        _blockMap = page.BlockMap;
        _lineShift = page.LineShift;
        _frozenEditSlot = null;
        _frame = frame;
        _panelSource = page.Source;
        return true;
    }

    /// <summary>What the block panel's surroundings depend on: while a render
    /// keeps all of it, only its blocks need updating.</summary>
    private sealed record PreviewFrame(DocumentViewModel Doc, string? Folder, AvaloniaRenderTheme Theme, PdfRenderOptions? Style,
        bool ShowTitleAndToc, string Header, double FitViewportWidth);

    // Stamped from the clock on every parse; they must not force a full rebuild.
    private static readonly HashSet<string> ClockAttributes = new(StringComparer.Ordinal)
    {
        "docyear", "docdate", "doctime", "docdatetime",
        "localyear", "localdate", "localtime", "localdatetime",
    };

    /// <summary>The title and attributes, which feed the title line, the title page and the style.</summary>
    private static string HeaderKey(DocumentNode document)
        => string.Join("\n", document.Attributes
            .Where(a => !ClockAttributes.Contains(a.Key))
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => $"{a.Key}={a.Value}")
            .Prepend(document.Title ?? string.Empty));

    /// <summary>
    /// Updates the shown block panel to <paramref name="layout"/> in place:
    /// the unchanged blocks before and after the edit keep their visuals (and so
    /// skip rendering, styling and measuring), only the ones between are
    /// rebuilt. False when the shown panel can't be patched.
    /// </summary>
    private bool TryReuseBlocks(DocumentLayout layout)
    {
        if (_renderedRoot is not StackPanel panel || _blockMap.Count == 0)
            return false;
        var old = _blockMap;
        var blocks = layout.Children;
        // The panel holds an optional title line, the block visuals, then a bottom spacer.
        var lead = panel.Children.Count - old.Count - 1;
        if (lead < 0)
            return false;
        var frozen = _frozenEditSlot is { } slot ? panel.Children.IndexOf(slot) - lead : -1;
        for (var i = 0; i < old.Count; i++)
            if (i != frozen && panel.Children[lead + i] != old[i].Vis)
                return false;

        var same = 0;
        while (same < old.Count && same < blocks.Count && same != frozen
               && LayoutEquality.Same(old[same].Block, blocks[same]))
            same++;
        var sameAfter = 0;
        while (sameAfter < old.Count - same && sameAfter < blocks.Count - same && old.Count - 1 - sameAfter != frozen
               && LayoutEquality.Same(old[^(sameAfter + 1)].Block, blocks[blocks.Count - 1 - sameAfter]))
            sameAfter++;

        var map = new List<(Control Vis, BlockLayout Block)>(blocks.Count);
        var shifts = new Dictionary<Control, int>();
        for (var i = 0; i < same; i++)
            map.Add(Kept(old[i], blocks[i], shifts));
        var fresh = new List<Control>();
        for (var i = same; i < blocks.Count - sameAfter; i++)
        {
            if (_avaloniaRenderer.Render(blocks[i]) is not { } vis) continue;
            fresh.Add(vis);
            map.Add((vis, blocks[i]));
        }
        for (var i = sameAfter; i > 0; i--)
            map.Add(Kept(old[^i], blocks[blocks.Count - i], shifts));

        panel.Children.RemoveRange(lead + same, old.Count - same - sameAfter);
        panel.Children.InsertRange(lead + same, fresh);
        _blockMap = map;
        _lineShift = shifts;
        _frozenEditSlot = null;
        // Re-measured from scratch on the next layout pass (MapAfterNextLayout).
        panel.MinHeight = 0;
        panel.InvalidateMeasure();
        return true;
    }

    /// <summary>A kept visual now standing for <paramref name="block"/>, which
    /// may sit lower or higher in the source than when the visual was rendered.</summary>
    private (Control Vis, BlockLayout Block) Kept(
        (Control Vis, BlockLayout Block) old, BlockLayout block, Dictionary<Control, int> shifts)
    {
        var shift = _lineShift.GetValueOrDefault(old.Vis);
        if (!block.Source.IsNone && !old.Block.Source.IsNone)
            shift += block.Source.Start.Line - old.Block.Source.Start.Line;
        if (shift != 0)
            shifts[old.Vis] = shift;
        return (old.Vis, block);
    }

    /// <summary>An inline's stamped source range, moved to where its block sits now.</summary>
    internal SourceRange CurrentRange(Visual from, SourceRange range)
    {
        if (range.IsNone || _lineShift.Count == 0) return range;
        for (var v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is Control c && _lineShift.TryGetValue(c, out var shift))
                return new SourceRange(Shift(range.Start), Shift(range.End));

            SourcePosition Shift(SourcePosition p) => p.IsNone ? p : p with { Line = p.Line + shift };
        }
        return range;
    }

    /// <summary>
    /// Build the (sourceLine → Y) map once the visual tree has been laid
    /// out. LayoutUpdated fires whenever any descendant is measured/arranged
    /// — we attach once and detach inside the handler so it only fires for
    /// the next layout pass.
    /// </summary>
    private void MapAfterNextLayout()
    {
        _lineMap = null;
        EventHandler? handler = null;
        handler = (s, e) =>
        {
            PreviewScroll.LayoutUpdated -= handler!;

            // Walk the ENTIRE visual subtree (not just direct children)
            // looking for the deepest-paint Y. If anything paints past
            // the root panel's reported height, force MinHeight to
            // accommodate it.
            if (_renderedRoot is Panel panel)
            {
                var deepBottom = FindDeepestBottom(panel);
                if (deepBottom > panel.Bounds.Height + 0.5)
                    panel.MinHeight = deepBottom;
            }

            _lineMap = BuildLineMap();

            // A click that committed an edit and landed on another block (or
            // table cell) queued it; now that the map + _astToEditor are
            // rebuilt, open it. Posted so it runs after this layout settles.
            // Not on a page of older text: a later change (a new row) is still to render.
            if (_pendingReentry is { } reentry && _doc?.SourceText == _panelSource)
            {
                _pendingReentry = null;
                Dispatcher.UIThread.Post(() => ConsumeReentry(reentry));
            }
            // A block moved or deleted from the keyboard: the keyboard goes on with it,
            // unless it went elsewhere meanwhile.
            if (_refocusCurrent && _doc?.SourceText == _panelSource)
            {
                _refocusCurrent = false;
                var turnInto = _turnIntoAfterRender;
                _turnIntoAfterRender = false;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_activeEdit is null && (FocusedElement is null || PreviewScroll.IsKeyboardFocusWithin))
                    {
                        FocusBlock(Math.Min(_current, _blockMap.Count - 1));
                        if (turnInto) ShowTurnIntoMenu(_current);
                    }
                });
            }
            if (FindBar.IsVisible) FindMatches(keepPlace: true); // the new text's matches, at the same one

            // Re-apply the last sync position so the preview doesn't jump
            // to the top after each re-render. This is the app driving the
            // preview, NOT a user scroll — suppress reverse-sync so the
            // resulting offset change doesn't bounce back and yank the
            // editor (toward the error line, where a parse error truncates
            // the line-map and any stray sync resolves).
            if (_lastSyncTotal > 0)
            {
                SuppressReverseSync = true;
                SyncToSourceLine(_lastSyncLine, _lastSyncFrac, _lastSyncTotal, _lastSyncToEnd);
                Dispatcher.UIThread.Post(
                    () => SuppressReverseSync = false, DispatcherPriority.Background);
            }

            // In WYSIWYG a tab just shown goes back to where it was; in the
            // other modes the source does it and the preview follows.
            if (_restoreTopLine)
            {
                _restoreTopLine = false;
                if (_mainVm?.EditorMode == EditorMode.Wysiwyg && _doc is not null)
                {
                    SuppressReverseSync = true;
                    if (_doc.TopLine > 0)
                        ScrollToSourceLine(_doc.TopLine, SourceLineCount(_doc));
                    else
                        PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, 0);
                    Dispatcher.UIThread.Post(
                        () => SuppressReverseSync = false, DispatcherPriority.Background);
                }
            }
        };
        PreviewScroll.LayoutUpdated += handler;
    }

    private static int SourceLineCount(DocumentViewModel doc) => doc.SourceText.AsSpan().Count('\n') + 1;

    /// <summary>In WYSIWYG the page is the view: its top line is the tab's position.</summary>
    private void RememberTopLine()
    {
        if (_restoreTopLine || _doc is null || _lineMap is null || _mainVm?.EditorMode != EditorMode.Wysiwyg) return;
        _doc.TopLine = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            PreviewScroll.Offset.Y, _lineMap, SourceLineCount(_doc), PreviewScroll.Extent.Height).Line;
    }

    /// <summary>
    /// A hairline in the theme's border colour around a white page: the preview
    /// shares the editor's surface, and in the light theme that surface is
    /// almost white, so the edge is what reads as "a sheet of paper".
    /// </summary>
    private Border PageEdge(Border page)
    {
        page.BorderThickness = new Thickness(1);
        page.CornerRadius = new CornerRadius(3);
        page.ClipToBounds = true;
        page.Bind(Border.BorderBrushProperty, this.GetResourceObservable("ForgeBorderBrush"));
        return page;
    }

    /// <summary>
    /// Zoom scales the whole preview (page width included), so a larger zoom
    /// also uses more of the window's width. Fit Width scales the page to the
    /// pane instead, keeping its line breaks.
    /// </summary>
    private LayoutTransformControl Zoomed(Control child, double pageWidth, DocumentViewModel doc)
    {
        var zoom = doc.ZoomLevel / 100.0;
        if (_mainVm?.PreviewFitWidth == true && PreviewScroll.Viewport.Width > 0)
            zoom = Math.Clamp((PreviewScroll.Viewport.Width - PreviewHost.Margin.Left - PreviewHost.Margin.Right
                - 2 * FitWidthGap) / pageWidth, 0.25, 4);
        _renderedViewportWidth = PreviewScroll.Viewport.Width;
        return new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(zoom, zoom),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = child,
        };
    }

    /// <summary>
    /// Render the document as the PDF export's pages (print-like preview):
    /// AdocNet lays the pages out with the active style (title page, running
    /// content, TOC page numbers), off the UI thread, then draws them. The
    /// interactive state (block map, line map) is cleared — this is a reading
    /// mode — and the scroll offset is preserved across re-renders so typing
    /// in Split mode doesn't snap the pages back to the top.
    /// </summary>
    private async void RenderPagePreview(DocumentViewModel doc, DocumentLayout layout, PdfRenderOptions pdf, int generation)
    {
        if (_pageLayoutRunning)
        {
            _pageLayoutQueued = true;
            return;
        }
        _pageLayoutRunning = true;
        try
        {
            await LayOutPagesAsync(doc, layout, pdf, generation);
        }
        finally
        {
            _pageLayoutRunning = false;
            if (_pageLayoutQueued)
            {
                _pageLayoutQueued = false;
                if (_doc is not null) UpdatePreview(_doc);
            }
        }
    }

    private async Task LayOutPagesAsync(DocumentViewModel doc, DocumentLayout layout, PdfRenderOptions pdf, int generation)
    {
        var savedOffset = PreviewScroll.Offset.Y;
        var document = doc.RenderedDocument!;
        try
        {
            var pages = await Task.Run(() => new PdfRenderer().LayoutPages(document, pdf));
            if (generation != _renderGeneration)
                return;
            Shown = Zoomed(
                _pagedRenderer.Render(layout, pages, PageLayoutOptions.FromPdf(pdf)),
                pdf.PageWidth * PreviewStyle.DipPerPoint, doc);
        }
        catch (Exception ex)
        {
            // async void: an escaping exception would end the app.
            if (generation != _renderGeneration) return;
            Shown = null;
            StatusOverlay.IsVisible = true;
            StatusText.Text = string.Format(Strings.StatusPagePreviewFailed, ex.Message);
            return;
        }
        _renderedRoot = null;
        _blockMap = [];
        _lineShift = new();
        _lineMap = null;

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            PreviewScroll.LayoutUpdated -= handler!;
            var max = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
            // A tab just shown goes back to its own place in the pages; a re-render keeps the view's.
            var y = _restoreTopLine ? doc.PageScroll * PreviewScroll.Extent.Height : savedOffset;
            _restoreTopLine = false;
            SuppressReverseSync = true;
            PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, Math.Min(y, max));
            Dispatcher.UIThread.Post(() => SuppressReverseSync = false, DispatcherPriority.Background);
        };
        PreviewScroll.LayoutUpdated += handler;
    }

    /// <summary>
    /// Fires when the user scrolls the preview manually: maps the current
    /// scroll offset back to a source line via the inverse of the line map
    /// and raises <see cref="SourceLineRequested"/>. Suppressed while the
    /// host is driving the preview from the editor.
    /// </summary>
    private void OnPreviewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (SuppressReverseSync) return;
        // Only respond to a genuine user scroll, i.e. an actual change in the
        // vertical OFFSET. A re-render that grows or shrinks the preview reports
        // an ExtentDelta with no OffsetDelta — reacting to that would reverse-
        // sync the editor on every multi-line insertion/auto-complete, yanking
        // the caret to whatever line the new offset/extent ratio maps to (near a
        // parse error, the truncated line-map resolves that to the error line).
        if (Math.Abs(e.OffsetDelta.Y) < 0.5)
            return;
        if (_mainVm?.PagePreview == true && _doc is not null && PreviewScroll.Extent.Height > 0)
            _doc.PageScroll = Math.Clamp(PreviewScroll.Offset.Y / PreviewScroll.Extent.Height, 0, 1);
        RememberTopLine();
        if (_lastSyncTotal <= 1) return;

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            PreviewScroll.Offset.Y, _lineMap, _lastSyncTotal, PreviewScroll.Extent.Height);
        SourceLineRequested?.Invoke(line, frac);
    }

    /// <summary>
    /// A press in the preview: commits an open in-place edit, opens the block
    /// menus on a right-click, and otherwise opens the clicked block (or table
    /// cell) in place. In Split the source pane jumps to the click too.
    /// </summary>
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ClearSelection();
        var src = e.Source as Visual;
        var inScrollBar = IsInScrollBar(src);
        if (HandlePressDuringEdit(src, inScrollBar, e) || inScrollBar) return;

        // Page preview is a reading mode: no block editing, no insert menus —
        // the block map isn't built there, so the gestures would misfire.
        if (_mainVm?.PagePreview == true) return;

        // Block gestures, resolved with the same _blockMap hit-test as
        // click-to-source tier 2: right-click opens the block context menu.
        var clickPoint = e.GetPosition(PreviewScroll);

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (TryHitBlock(src, clickPoint, out var menuVis, out var menuBlock))
                ShowBlockContextMenu(menuVis, menuBlock, src, clickPoint);
            else
                ShowInsertMenuAt(clickPoint);  // empty area / gap / empty doc
            e.Handled = true;
            return;
        }

        // A click opens the clicked block (or, in a table, the clicked cell's
        // content) in place, with the caret at the clicked character. In Split
        // the source pane jumps there first; the jump leaves the keyboard alone,
        // so the in-place editor takes it.
        if (_mainVm?.EditorMode == EditorMode.Split)
            JumpToSource(src, e);
        if (TryHitBlock(src, clickPoint, out var vis, out var block))
        {
            var entered = block is TableLayout table
                ? EnterCellEdit(vis, table, src, clickPoint, RenderedCharOffset(src, e))
                : EnterBlockEdit(vis, block, ResolveClickedBlockOffset(src, e));
            if (entered)
                e.Handled = true;
        }
    }

    /// <summary>
    /// Click-to-source, in three tiers of decreasing precision:
    /// <list type="number">
    ///   <item>Inline hit-test — the <see cref="TextBlock"/> under the pointer
    ///     is hit-tested to the exact rendered <see cref="Inline"/>, whose
    ///     stamped <see cref="AvaloniaRenderer.SourceRangeProperty"/> is now
    ///     ABSOLUTE (AdocNet 1.0.10, upstream #38) — gives line AND column, so
    ///     the caret lands on the clicked word.</item>
    ///   <item>Block hit-test — the click's top-level block (or table row) via
    ///     <see cref="_blockMap"/>, using its absolute source line (column 0).
    ///     Covers clicks on cell padding / block chrome with no inline.</item>
    ///   <item>Line-map inverse by Y — clicks in inter-block gaps.</item>
    /// </list>
    /// </summary>
    private void JumpToSource(Visual? src, PointerPressedEventArgs e)
    {
        if (_lastSyncTotal <= 1) return;

        var pPreview = e.GetPosition(PreviewScroll);

        // Tier 1: exact inline hit-test → absolute line + column.
        var tb = FindAncestorTextBlock(src);
        if (tb is not null && tb.Inlines is { Count: > 0 })
        {
            var sr = SourceRange.None;
            try
            {
                var ptInTb = e.GetPosition(tb);
                var hit = tb.TextLayout.HitTestPoint(ptInTb);
                int pos = 0;
                if (!TryFindInlineRange(tb.Inlines, ref pos, hit.TextPosition, SourceRange.None, out sr))
                    sr = SourceRange.None;
                sr = CurrentRange(tb, sr);
            }
            catch { sr = SourceRange.None; }

            if (!sr.IsNone)
            {
                var editorLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, sr.Start.Line - 1, fallback: -1);
                if (editorLine >= 0)
                {
                    var col = Math.Max(0, sr.Start.Column - 1);
                    SourceLineClicked?.Invoke(editorLine, col, pPreview.Y);
                    return;
                }
            }
        }

        // Tier 2: exact block hit — find the top-level block whose visual
        // contains the click target, then resolve to its (or its table row's)
        // absolute source line.
        foreach (var (vis, block) in _blockMap)
        {
            if (!IsAncestorOrSelf(vis, src)) continue;

            var chosen = block.Source;

            if (block is TableLayout table)
            {
                var grid = vis as Grid ?? FindDescendantGrid(vis);
                var row = grid is not null ? FindGridRowOf(grid, src) : -1;
                if (row >= 0 && row < table.Rows.Count && !table.Rows[row].Source.IsNone)
                    chosen = table.Rows[row].Source;
            }

            if (!chosen.IsNone)
            {
                var editorLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, chosen.Start.Line - 1, fallback: -1);
                if (editorLine >= 0)
                {
                    SourceLineClicked?.Invoke(editorLine, 0, pPreview.Y);
                    return;
                }
            }
            break; // click is inside this block but it's unmapped (e.g. from
                   // an include); fall through to the line-map inverse.
        }

        // Tier 3: line-map inverse by Y (click in inter-block gap or on a
        // block with no source range).
        if (_lineMap is null || _lineMap.Count == 0) return;
        var contentY = pPreview.Y + PreviewScroll.Offset.Y;
        var (line, _) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            contentY, _lineMap, _lastSyncTotal, PreviewScroll.Extent.Height);
        SourceLineClicked?.Invoke(line, 0, pPreview.Y);
    }

    /// <summary>
    /// While an in-place edit is open: presses inside the inline editor belong
    /// to it, scrollbar presses keep it alive (the user may just be scrolling to
    /// check context), and any other press commits it (the preview's content is
    /// mostly non-focusable, so LostFocus alone doesn't fire for clicks into dead
    /// space). True when the press was dealt with here.
    /// </summary>
    private bool HandlePressDuringEdit(Visual? src, bool inScrollBar, PointerPressedEventArgs e)
    {
        if (_activeEdit is null) return false;
        if (inScrollBar || IsAncestorOrSelf(_activeEdit.Host, src))
            return true;

        // A left-click on ANOTHER block queues that block — or, inside a table,
        // the specific clicked cell — so the same click also opens it.
        _pendingReentry = null;
        if (_doc is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var cp = e.GetPosition(PreviewScroll);
            if (TryHitBlock(src, cp, out var nextVis, out var nextBlock)
                && BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, nextBlock.Source, _astToEditor)
                    is (int ns, _))
            {
                if (nextBlock is TableLayout nextTable
                    && TryResolveCell(nextVis, nextTable, src, cp, out _, out var rr, out var ci))
                    _pendingReentry = (ns, rr, ci, false);
                else if (nextBlock is not TableLayout)
                    _pendingReentry = (ns, -1, -1, false);
            }
        }

        // The press also moves the keyboard to the page, whose LostFocus may commit
        // first and open the queued block: that one stays open.
        var pressed = _activeEdit;
        Dispatcher.UIThread.Post(() => { if (_activeEdit == pressed) CommitBlockEdit(); });
        e.Handled = true;
        return true;
    }

    // ── Block-WYSIWYG: in-place editing ──────────────────────────────────
    //
    // Modeled on the upstream samples/AdocNet.Avalonia.Editor: a
    // clicked block's rendered visual is swapped in the panel for a small
    // AvaloniaEdit editor seeded with the block's source slice; Ctrl+Enter
    // or clicking away commits the slice back into DocumentViewModel
    // .SourceText (single source of truth — dirty tracking and the debounced
    // re-render then replace the inline editor with the fresh block);
    // Escape restores the original visual without touching the source.
    // Unlike the upstream sample, slices are resolved through the include-
    // expansion map, so blocks that originate in include::d files are
    // detected and refused instead of corrupting the wrong lines.

    /// <summary>
    /// Find the top-level rendered block the click belongs to. Tries visual
    /// ancestry first (precise — the click landed on rendered content), then
    /// falls back to geometry: the block whose rendered bounds contain the
    /// click point. The geometric pass is what lets a click anywhere in a
    /// block — including the transparent gaps inside a table cell, which
    /// aren't hit-test visible — resolve to that block.
    /// </summary>
    private bool TryHitBlock(Visual? from, Point pointInScroll, out Control vis, out BlockLayout block)
    {
        foreach (var (v, b) in _blockMap)
        {
            if (IsAncestorOrSelf(v, from))
            {
                vis = v;
                block = b;
                return true;
            }
        }

        foreach (var (v, b) in _blockMap)
        {
            if (PreviewScroll.TranslatePoint(pointInScroll, v) is Point local
                && new Rect(v.Bounds.Size).Contains(local))
            {
                vis = v;
                block = b;
                return true;
            }
        }

        vis = null!;
        block = null!;
        return false;
    }

    /// <summary>
    /// Swap a rendered block's visual for an in-place source editor covering
    /// the whole block. <paramref name="caretAbsOffset"/>, when given, is the
    /// absolute editor offset of the clicked character — the inline editor's
    /// caret is placed there. Returns false when the block can't be edited (no
    /// source range, or it originates in an included file).
    /// </summary>
    private bool EnterBlockEdit(Control blockVis, BlockLayout block, int? caretAbsOffset = null)
    {
        if (_doc is null) return false;

        // Entering an edit supersedes any queued chain-re-entry.
        _pendingReentry = null;
        if (!PanelIsCurrent()) return false;

        var text = _doc.SourceText;
        var slice =BlockSourceEditing.ResolveEditorSlice(text, block.Source, _astToEditor);
        if (slice is null)
        {
            if (_mainVm is not null)
                _mainVm.StatusText = Strings.StatusBlockFromInclude;
            return false;
        }

        if (_renderedRoot is not Panel panel) return false;
        var index = panel.Children.IndexOf(blockVis);
        if (index < 0) return false;
        MakeCurrent(blockVis);

        var (start, length) = slice.Value;
        var original = text.Substring(start, length);

        var (editor, host) = BuildInlineEditor(original, compact: false);
        panel.Children[index] = host;

        BeginEdit(editor, host, start, length, original,
            restore: () =>
            {
                var idx = panel.Children.IndexOf(host);
                if (idx >= 0) panel.Children[idx] = blockVis;
            },
            statusHint: Strings.StatusEditingBlock,
            caretInSlice: caretAbsOffset is int abs ? abs - start : null);
        return true;
    }

    /// <summary>
    /// Swap a single table cell's content for an in-place editor. The cell's
    /// content span is located inside the row's source slice (AdocNet only
    /// exposes row-level source ranges — see upstream issue on per-cell
    /// ranges), so only the content between the cell's delimiters is edited;
    /// specs and pipes stay untouched.
    /// </summary>
    private bool EnterCellEdit(Control tableVis, TableLayout table, Visual? clickTarget,
        Point pointInScroll, int? caretInContent = null)
    {
        if (_doc is null) return false;
        MakeCurrent(tableVis);
        if (!TryResolveCell(tableVis, table, clickTarget, pointInScroll,
                out var cellControl, out var r, out var cellIdx))
            return false;
        return EnterCellEditCore(cellControl, table, r, cellIdx, caretInContent);
    }

    /// <summary>Open a cell for editing addressed by (row, column) instead of a
    /// click — re-derives the cell control from the grid by flattening the
    /// row/column into the child index (the inverse of
    /// <see cref="TryResolveCell"/>). Used to chain a commit into the
    /// just-clicked cell after the table re-renders.</summary>
    private bool EnterCellEditByIndex(Control tableVis, TableLayout table, int rowIndex, int cellIndex, int? caretInContent = null)
    {
        MakeCurrent(tableVis);
        var grid = tableVis as Grid ?? FindDescendantGrid(tableVis);
        if (grid is null) return false;

        var cellsPerRow = table.Rows.Select(r => r.Cells.Count).ToList();
        if (TableStructure.FlatCellIndex(cellsPerRow, rowIndex, cellIndex) is not int flat)
            return false;
        if (flat < 0 || flat >= grid.Children.Count) return false;
        if (grid.Children[flat] is not Control cellControl) return false;
        return EnterCellEditCore(cellControl, table, rowIndex, cellIndex, caretInContent);
    }

    /// <summary>A cell's slice of the source. An empty Markdown cell has no range of its own: it is found between its pipes.</summary>
    private (int Start, int Length)? CellSlice(TableLayout table, int r, int c)
    {
        if (_doc is null) return null;
        var cell = table.Rows[r].Cells[c];
        return BlockSourceEditing.ResolveCellSlice(_doc.SourceText, cell.Source, _astToEditor)
            ?? (_doc.IsMarkdown && cell.Source.IsNone
                ? BlockSourceEditing.EmptyMarkdownCell(_doc.SourceText, table.Source, r, c, _astToEditor) : null);
    }

    private bool EnterCellEditCore(Control cellControl, TableLayout table, int r, int cellIdx,
        int? caretInContent)
    {
        if (_doc is null) return false;

        // Entering an edit supersedes any queued chain-re-entry.
        _pendingReentry = null;
        if (!PanelIsCurrent()) return false;

        var text = _doc.SourceText;
        // AdocNet 1.0.13 (#45) exposes a per-cell Source range covering exactly
        // the cell's content (no delimiters, no cell-spec prefix), so we splice
        // it directly — only an empty Markdown cell is looked for in its row.
        var cellSlice = CellSlice(table, r, cellIdx);
        if (cellSlice is null)
        {
            if (_mainVm is not null)
                _mainVm.StatusText = Strings.StatusCellFromInclude;
            return false;
        }

        var (start, length) = cellSlice.Value;
        var original = text.Substring(start, length);

        var (editor, host) = BuildInlineEditor(original, compact: true);

        Action restore;
        if (cellControl is Border cellBorder)
        {
            // Normal case: the renderer wraps each cell in a Border — swap
            // its child so the grid placement (row/col/spans) is untouched.
            var originalChild = cellBorder.Child;
            cellBorder.Child = host;
            restore = () => cellBorder.Child = originalChild;
        }
        else if (cellControl.GetVisualParent() is Grid cellGrid)
        {
            // Fallback: replace the grid child itself, carrying over the
            // attached placement properties.
            Grid.SetRow(host, Grid.GetRow(cellControl));
            Grid.SetColumn(host, Grid.GetColumn(cellControl));
            Grid.SetRowSpan(host, Grid.GetRowSpan(cellControl));
            Grid.SetColumnSpan(host, Grid.GetColumnSpan(cellControl));
            var slot = cellGrid.Children.IndexOf(cellControl);
            if (slot < 0) return false;
            cellGrid.Children[slot] = host;
            restore = () =>
            {
                var idx = cellGrid.Children.IndexOf(host);
                if (idx >= 0) cellGrid.Children[idx] = cellControl;
            };
        }
        else
        {
            return false;
        }

        BeginEdit(editor, host, start, length, original, restore,
            statusHint: Strings.StatusEditingCell,
            caretInSlice: caretInContent, cell: (r, cellIdx));
        if (_activeEdit?.Assist is { } assist) assist.CellTableEdit = op => TableEditAtCell(op);
        return true;
    }

    /// <summary>Common tail of both edit entry points: record state, place
    /// the caret, wire the commit/cancel handlers, focus the editor.</summary>
    private void BeginEdit(TextEditor editor, Control host, int start, int length,
        string originalSlice, Action restore, string statusHint, int? caretInSlice = null, (int Row, int Cell)? cell = null)
    {
        ClearSelection();
        _activeEdit = new BlockEditState
        {
            Editor = editor,
            Host = host,
            Restore = restore,
            Start = start,
            Length = length,
            OriginalSlice = originalSlice,
            WasDirty = _doc?.IsDirty ?? false,
            SourceAtOpen = _doc?.SourceText ?? string.Empty,
            Cell = cell,
            NewParagraph = _newParagraph is { } added && added.At == start && added.After == _doc?.SourceText
                ? (added.Before, added.WasDirty) : null,
            // The inline editor holds one block's slice, so feed the whole
            // document for anchor/attribute (document-aware) completion.
            Assist = new CompletionController(editor,
                documentSource: () => _doc?.SourceText ?? string.Empty)
            {
                Markdown = _doc?.IsMarkdown == true,
                DocumentFolder = () => _doc?.BaseDirectory,
            },
        };
        _newParagraph = null;

        if (caretInSlice is int c)
            editor.CaretOffset = Math.Clamp(c, 0, length);

        // Typed-but-uncommitted text is a change: closing must prompt for it,
        // and a crash snapshot must hold it.
        var doc = _doc;
        var source = doc?.SourceText ?? string.Empty;
        editor.TextChanged += (_, _) =>
        {
            if (doc is null) return;
            doc.IsDirty = true;
            doc.UnappliedEdit = new(source, start, length, editor.Text);
        };

        // KeyDown must TUNNEL: AvaloniaEdit's TextArea consumes Escape (and
        // would insert a newline for Enter variants) before a bubbling
        // handler ever sees the key. LostFocus bubbles, so the host hears it
        // from the TextArea.
        host.AddHandler(InputElement.KeyDownEvent, OnBlockEditKeyDown, RoutingStrategies.Tunnel);
        host.AddHandler(InputElement.KeyDownEvent, OnBlockEditArrow, RoutingStrategies.Bubble, handledEventsToo: true);
        host.LostFocus += OnBlockEditLostFocus;
        editor.Focus();

        // Focus the TextArea (the real input surface) once layout settles, so the
        // first keystroke lands — editor.Focus() alone leaves the programmatic
        // re-entry path (a fresh "New paragraph.") open but un-typeable. When a
        // placeholder was inserted, select its text so the first key replaces it.
        var selectAll = _selectAllOnNextEdit;
        _selectAllOnNextEdit = false;
        Dispatcher.UIThread.Post(() =>
        {
            editor.TextArea.Focus();
            if (selectAll) editor.SelectAll();
        }, DispatcherPriority.Input);

        if (_mainVm is not null)
            _mainVm.StatusText = statusHint;
    }

    /// <summary>
    /// Absolute editor offset of the character under the click, via the
    /// inline source ranges stamped on the rendered text (absolute since
    /// AdocNet 1.0.10). Returns null when the click isn't over a mapped
    /// inline (e.g. block chrome, or content from an include).
    /// </summary>
    private int? ResolveClickedBlockOffset(Visual? src, PointerPressedEventArgs e)
    {
        var tb = FindAncestorTextBlock(src);
        if (tb is null || tb.Inlines is not { Count: > 0 }) return null;

        int textPos;
        try { textPos = tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition; }
        catch { return null; }

        var pos = 0;
        if (!TryFindInlineRange(tb.Inlines, ref pos, textPos, SourceRange.None, out var sr) || sr.IsNone)
            return null;
        sr = CurrentRange(tb, sr);

        var editorLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, sr.Start.Line - 1, fallback: -1);
        if (editorLine < 0) return null;

        // Add the rendered offset INTO the matched run so the caret lands on
        // the clicked character, not the run's start. Exact for plain runs
        // (rendered text == source); approximate for styled runs (markup
        // chars make rendered shorter than source) — clamped to the line.
        var intra = Math.Max(0, textPos - pos);
        return EditorOffsetFromLineCol(editorLine, sr.Start.Column - 1 + intra);
    }

    /// <summary>
    /// Rendered character offset within the clicked cell's <see cref="TextBlock"/>,
    /// used as a best-effort caret for cell editing. (Cell inline ranges are
    /// cell-relative — upstream #45 — so source-range mapping isn't usable
    /// here; this is exact for plain cells.)
    /// </summary>
    private int? RenderedCharOffset(Visual? src, PointerPressedEventArgs e)
    {
        var tb = FindAncestorTextBlock(src);
        if (tb is null) return null;
        try { return tb.TextLayout.HitTestPoint(e.GetPosition(tb)).TextPosition; }
        catch { return null; }
    }

    /// <summary>Absolute offset in the current document's source for a 0-based
    /// (line, column), clamping the column to the line's content.</summary>
    private int? EditorOffsetFromLineCol(int editorLine, int editorColumn)
    {
        if (_doc is null) return null;
        var text = _doc.SourceText;

        var line = 0;
        var i = 0;
        while (line < editorLine && i < text.Length)
            if (text[i++] == '\n') line++;
        if (line < editorLine) return null;

        var lineStart = i;
        var nl = text.IndexOf('\n', lineStart);
        var lineEnd = nl < 0 ? text.Length : nl;
        return lineStart + Math.Clamp(editorColumn, 0, lineEnd - lineStart);
    }

    /// <summary>
    /// Build the in-place editor: an AvaloniaEdit editor over the slice, wrapped
    /// in a highlighted border. Apply and Cancel live in the toolbar, so even a
    /// narrow table cell holds nothing but the text.
    /// </summary>
    private (TextEditor Editor, Border Host) BuildInlineEditor(string slice, bool compact)
    {
        var editor = new AccessibleTextEditor(Strings.ModeSource)
        {
            Document = new TextDocument(slice),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            FontSize = compact ? 12 : 13,
            ShowLineNumbers = false,
            WordWrap = true,
            Background = Brushes.Transparent,
            Padding = compact ? new Thickness(4, 2) : new Thickness(8, 6),
        };

        var host = new Border
        {
            Background = new SolidColorBrush(Color.Parse(IsDarkTheme ? "#2E2A1C" : "#FFFBE6")),
            BorderBrush = new SolidColorBrush(Color.Parse(IsDarkTheme ? "#E8608A" : "#D94F7A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 2),
            Child = editor,
        };
        return (editor, host);
    }

    private void OnBlockEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeEdit is null) return;

        // While a completion popup or snippet session is up, let the assist
        // controller own Esc/Tab/Enter — don't cancel the block edit out from
        // under it. (This host handler tunnels first, so it must defer.)
        if (_activeEdit.Assist is { IsActive: true } assist)
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            assist.CloseList(); // Ctrl+Enter applies the block, never a suggestion
        }

        if (e.Key == Key.Escape)
        {
            CancelBlockEdit();
            FocusBlock(_current); // the editor is gone: the keyboard stays on its block
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // In a table cell: applied, and a new row below to type in.
            if (_activeEdit.Cell is not { } cell || !AddRowBelow(cell.Row))
            {
                CommitBlockEdit();
                FocusBlock(_current); // an applied change: once the block is rendered again
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && SplitBlock())
            e.Handled = true;
        else if (_activeEdit.Cell is null && IsTurnIntoKey(e))
            e.Handled = ShowTurnIntoMenu(_current);
        else if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control
                 && _activeEdit.Editor.SelectionLength == _activeEdit.Editor.Text.Length)
        {
            // All its text selected already: next its table (in a cell), then every block.
            var cell = _activeEdit.Cell is not null;
            var i = _current;
            CommitBlockEdit();
            _selectAnchor = cell ? i : 0;
            _current = cell ? i : _blockMap.Count - 1;
            if (_doc?.SourceText == _panelSource)
            {
                FocusBlock(_current, bringIntoView: cell);
                Select(_selectAnchor);
            }
            else _refocusCurrent = true;
            e.Handled = true;
        }
        else if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && JoinWithBlockAbove())
            e.Handled = true;
        else if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && _activeEdit.Cell is { } cell)
        {
            MoveFromCell(cell, vertical: false, e.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None
                 && _activeEdit.Editor.TextArea.Selection.IsEmpty)
            _caretBeforeArrow = _activeEdit.Editor.CaretOffset;
    }

    // Where the caret was before an arrow key: an arrow that can't move it leaves the block.
    private int? _caretBeforeArrow;

    /// <summary>After the editor's own handling: an arrow at the edge of a block (or a table cell) goes on to the one beside it.</summary>
    private void OnBlockEditArrow(object? sender, KeyEventArgs e)
    {
        var before = _caretBeforeArrow;
        _caretBeforeArrow = null;
        if (before is null || _activeEdit is not { } edit || edit.Editor.CaretOffset != before) return;
        var step = e.Key is Key.Up or Key.Left ? -1 : 1;
        if (edit.Cell is { } cell)
            MoveFromCell(cell, vertical: e.Key is Key.Up or Key.Down, step);
        else if (!LeaveBlock(step))
            return;
        e.Handled = true;
    }

    /// <summary>Enter in a paragraph or heading: two blocks, the text after the caret opening in the
    /// second. At the start, a new paragraph above; at the end, one below. Shift+Enter: a line.</summary>
    private bool SplitBlock()
    {
        if (_doc is null || _activeEdit is not { Cell: null } edit || _current >= _blockMap.Count
            || _blockMap[_current].Block is not (ParagraphLayout or HeadingLayout) || !edit.Editor.TextArea.Selection.IsEmpty)
            return false;
        if (edit.Assist?.SmartEnter() == true) return true; // a fence or a list item typed in: Enter's own job
        var text = edit.Editor.Text;
        var caret = edit.Editor.CaretOffset;
        var marker = _blockMap[_current].Block is HeadingLayout ? HeadingMarker().Match(text).Length : 0;
        var before = caret <= marker ? "" : text[..caret].TrimEnd();
        var after = text[caret..].TrimStart();
        if (before.Length == 0)
        {
            if (WholeBlock(_current) is not { } whole) return false;
            CommitBlockEdit();
            OpenNewParagraph(whole.Start);
        }
        else if (after.Length == 0)
        {
            CommitBlockEdit();
            OpenNewParagraph(edit.Start + text.Length);
        }
        else
        {
            edit.Editor.Text = before + "\n\n" + after;
            CommitBlockEdit();
            _pendingReentry = (edit.Start + before.Length + 2, -1, -1, false); // in the split text: not shifted by the commit
        }
        return true;
    }

    [GeneratedRegex(@"^(=+|#{1,6})[ \t]+")]
    private static partial Regex HeadingMarker();

    /// <summary>Backspace at the start of a paragraph: its text joins the paragraph or heading above,
    /// the caret where they meet. Not for a paragraph with a title or anchor of its own.</summary>
    private bool JoinWithBlockAbove()
    {
        if (_doc is null || _activeEdit is not { Cell: null } edit || edit.Editor.CaretOffset != 0
            || !edit.Editor.TextArea.Selection.IsEmpty || _current < 1 || _current >= _blockMap.Count
            || _blockMap[_current].Block is not ParagraphLayout || _blockMap[_current - 1].Block is not (ParagraphLayout or HeadingLayout)
            || WholeBlock(_current)?.Start != edit.Start
            || BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, _blockMap[_current - 1].Block.Source, _astToEditor) is not (int above, int aboveLength)
            || above + aboveLength > edit.Start
            || !_doc.SourceText.AsSpan(above + aboveLength, edit.Start - above - aboveLength).IsWhiteSpace())
            return false;
        var typed = edit.Editor.Text;
        CommitBlockEdit();
        var text = _doc.SourceText;
        if (!text.AsSpan(edit.Start).StartsWith(typed)) return true; // not applied: nothing to join
        _current--;
        _caretOnNextEdit = above + aboveLength;
        _pendingReentry = (above, -1, -1, false);
        _doc.SourceText = text[..(above + aboveLength)] + text[edit.Start..];
        return true;
    }

    /// <summary>Applies the open edit and opens the block beside it, a table at its first or last cell.
    /// Past the last block, a new paragraph. False at the top of the document.</summary>
    private bool LeaveBlock(int step)
    {
        if (_doc is null || _activeEdit is not { } edit) return false;
        var target = _current + step;
        if (target < 0) return false;
        if (target >= _blockMap.Count)
        {
            // Not a second one after a new paragraph left as it was.
            if (edit.Cell is null && edit.Editor.Text.Trim() == BlockSnippets.Paragraph) return false;
            CommitBlockEdit();
            OpenNewParagraph(_doc.SourceText.Length);
            return true;
        }
        var block = _blockMap[target].Block;
        // Opened like a click on another block: at once, or after the applied change's re-render.
        _pendingReentry = BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, block.Source, _astToEditor) is not (int s, _) ? null
            : block is TableLayout table && EdgeCell(table, step) is var (r, c) ? (s, r, c, AtEnd: step < 0)
            : (s, -1, -1, AtEnd: step < 0);
        _current = target;
        CommitBlockEdit();
        if (_pendingReentry is null && _activeEdit is null) FocusBlock(target); // a block from an included file
        return true;
    }

    /// <summary>The cell an arrow enters a table by: the first one going down, the last one going up.</summary>
    private static (int Row, int Cell)? EdgeCell(TableLayout table, int step)
    {
        var rows = Enumerable.Range(0, table.Rows.Count).Where(r => table.Rows[r].Cells.Count > 0);
        if (!rows.Any()) return null;
        var row = step > 0 ? rows.First() : rows.Last();
        return (row, step > 0 ? 0 : table.Rows[row].Cells.Count - 1);
    }

    /// <summary>From the open cell to the one beside it: along the rows (Tab, Left, Right) or up and
    /// down a column (Up, Down). Past the table's first or last cell, on to the block beside it.</summary>
    private void MoveFromCell((int Row, int Cell) from, bool vertical, int step)
    {
        if (_doc is null || _current >= _blockMap.Count || _blockMap[_current].Block is not TableLayout table) return;
        var rows = table.Rows;
        var (r, c) = (from.Row, from.Cell + step);
        if (vertical)
        {
            if (TableStructure.CellAcross([.. rows.Select(row => (IReadOnlyList<(int, int)>)[.. row.Cells.Select(cell => (cell.ColSpan, cell.RowSpan))])],
                    from, step) is not { } across)
            {
                LeaveBlock(step);
                return;
            }
            (r, c) = across;
        }
        else if (c < 0 || c >= rows[r].Cells.Count)
        {
            do r += step; while (r >= 0 && r < rows.Count && rows[r].Cells.Count == 0); // rows all spanned from above
            if (r < 0 || r >= rows.Count)
            {
                LeaveBlock(step);
                return;
            }
            c = step > 0 ? 0 : rows[r].Cells.Count - 1;
        }
        if (BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, table.Source, _astToEditor) is not (int s, _)) return;
        _pendingReentry = (s, r, c, AtEnd: step < 0);
        CommitBlockEdit();
    }

    /// <summary>Applies the open cell and adds a row below it, then opens the new row's first cell.</summary>
    private bool AddRowBelow(int row)
    {
        if (_doc is null || _current >= _blockMap.Count
            || BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, _blockMap[_current].Block.Source, _astToEditor) is not (int s, _)
            || !TableEditAtCell(TableOp.RowBelow))
            return false;
        _pendingReentry = (s, row + 1, 0, false);
        return true;
    }

    /// <summary>Puts a new paragraph after source offset <paramref name="end"/> and opens it, its placeholder selected.</summary>
    private void OpenNewParagraph(int end)
    {
        if (_doc is null) return;
        var text = _doc.SourceText;
        end = Math.Clamp(end, 0, text.Length);
        // Where BlockInsertion.Join puts it: one blank line after the text before.
        var before = text.AsSpan(0, end).TrimEnd('\n').Length;
        var at = before == 0 ? 0 : before + 2;
        var after = BlockInsertion.InsertAfter(text, end, BlockSnippets.Paragraph);
        _selectAllOnNextEdit = true;
        _pendingReentry = (at, -1, -1, false);
        _newParagraph = (text, after, at, _doc.IsDirty);
        _doc.SourceText = after;
    }

    private void OnBlockEditLostFocus(object? sender, RoutedEventArgs e)
    {
        // Defer one dispatcher tick so the keystroke/click that moved focus
        // finishes processing before the editor is torn down. After the
        // tick, skip the commit when focus merely moved WITHIN the inline
        // editor host. (The toolbar's Apply and Cancel never take the
        // keyboard, so pressing them doesn't commit here first.)
        Dispatcher.UIThread.Post(() =>
        {
            var edit = _activeEdit;
            if (edit is null) return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused is not null && IsAncestorOrSelf(edit.Host, focused)) return;
            // The editor's right-click menu holds the keyboard while it is open.
            if (focused?.FindAncestorOfType<ContextMenu>(includeSelf: true) is not null) return;
            CommitBlockEdit();
        });
    }

    private void CommitBlockEdit()
    {
        var edit = _activeEdit;
        if (edit is null || _doc is null) return;

        edit.Host.RemoveHandler(InputElement.KeyDownEvent, OnBlockEditKeyDown);
        edit.Host.RemoveHandler(InputElement.KeyDownEvent, OnBlockEditArrow);
        edit.Host.LostFocus -= OnBlockEditLostFocus;
        _activeEdit = null;

        // An editor of one line breaks lines the system's way: here, the document's.
        var newSlice = edit.Editor.Text.ReplaceLineEndings(_doc.SourceText.Contains("\r\n") ? "\r\n" : "\n");
        var (action, s, len) = BlockSourceEditing.DecideCommit(
            _doc.SourceText, edit.Start, edit.Length, edit.OriginalSlice, newSlice);

        if (action == BlockSourceEditing.CommitAction.Replace)
        {
            // Freeze the inline editor until the debounced re-render replaces
            // the panel — the splice below kicks that off via dirty tracking.
            edit.Editor.IsReadOnly = true;
            _frozenEditSlot = _renderedRoot is Panel panel
                ? edit.Host.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => c.GetVisualParent() == panel)
                : null;
            _doc.SourceText = _doc.SourceText.Remove(s, len).Insert(s, newSlice);
            if (_mainVm is not null) _mainVm.StatusText = Strings.StatusBlockUpdated;
            _rebuildPending = false; // the splice's own re-render rebuilds the panel

            // Shift a queued re-entry for the splice: a block above the edit is
            // unchanged; one below moves by the length delta. Consumed once the
            // re-render rebuilds the block map (see UpdatePreview).
            if (_pendingReentry is { } p)
            {
                var shifted = BlockSourceEditing.ShiftOffsetForSplice(p.EditorStart, s, len, newSlice.Length);
                _pendingReentry = p with { EditorStart = shifted };
            }
        }
        else
        {
            edit.Restore();
            UndoTypingDirty(edit);
            if (_mainVm is not null)
                _mainVm.StatusText = action == BlockSourceEditing.CommitAction.Abort
                    ? Strings.StatusEditDiscarded
                    : Strings.Ready;

            // No source change → no re-render to host the re-entry; open the
            // queued target now against the still-current block map — unless
            // a postponed rebuild runs now, whose layout pass consumes it.
            if (_rebuildPending)
                UpdatePreview(_doc);
            else if (_pendingReentry is { } p)
            {
                _pendingReentry = null;
                ConsumeReentry(p);
            }
        }
    }

    /// <summary>Open the queued chain-re-entry target: re-find its block by
    /// editor offset, then open the whole block or the specific table cell.</summary>
    private void ConsumeReentry((int EditorStart, int RowIndex, int CellIndex, bool AtEnd) req)
    {
        var target = FindBlockByEditorStart(req.EditorStart);
        if (target is null) return;

        var (vis, block) = target.Value;
        if (_mainVm?.EditorMode == EditorMode.Split && _doc is not null)
        {
            // The click that queued this block jumps the source pane to it too.
            var text = _doc.SourceText;
            var at = Math.Clamp(req.EditorStart, 0, text.Length);
            var lineStart = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
            var y = vis.TranslatePoint(default, PreviewScroll)?.Y ?? 0;
            SourceLineClicked?.Invoke(text.AsSpan(0, lineStart).Count('\n'), at - lineStart, y);
        }
        var caret = _caretOnNextEdit;
        _caretOnNextEdit = null;
        var opened = block is TableLayout tbl
            ? req.CellIndex >= 0 && EnterCellEditByIndex(vis, tbl, req.RowIndex, req.CellIndex, req.AtEnd ? int.MaxValue : null)
            : EnterBlockEdit(vis, block, caret ?? (req.AtEnd ? int.MaxValue : null));
        // A block it can't open (from an included file) is stopped on instead.
        if (!opened) FocusBlock(_blockMap.FindIndex(b => b.Vis == vis));
    }

    /// <summary>Locate the block in the current map whose editor slice starts at
    /// <paramref name="editorStart"/> (exact, else within a couple of chars to
    /// absorb mapping rounding). Null if none qualifies.</summary>
    private (Control Vis, BlockLayout Block)? FindBlockByEditorStart(int editorStart)
    {
        if (_doc is null) return null;

        (Control, BlockLayout)? best = null;
        var bestDelta = int.MaxValue;
        foreach (var (vis, block) in _blockMap)
        {
            if (BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, block.Source, _astToEditor)
                is not (int s, _))
                continue;
            var d = Math.Abs(s - editorStart);
            if (d < bestDelta) { bestDelta = d; best = (vis, block); }
        }
        return bestDelta <= 2 ? best : null;
    }

    /// <summary>Typing marked the document dirty; an edit that changed nothing undoes that.</summary>
    private void UndoTypingDirty(BlockEditState edit)
    {
        if (_doc is not null && !edit.WasDirty && _doc.SourceText == edit.SourceAtOpen)
            _doc.IsDirty = false;
    }

    /// <summary>A table edit from the open cell editor: keep the cell's text,
    /// then edit the table around it.</summary>
    private bool TableEditAtCell(TableOp op)
    {
        if (_activeEdit is not { } edit || _doc is null) return false;
        var typed = edit.Editor.Text;
        CommitBlockEdit(); // the splice starts where the cell's text did
        // A commit refused because the text changed meanwhile leaves the offset stale.
        var at = _doc.SourceText.AsSpan(Math.Min(edit.Start, _doc.SourceText.Length));
        return (at.StartsWith(typed) || at.StartsWith(edit.OriginalSlice)) && TableEditAt(edit.Start, op);
    }

    /// <summary>Edit the table around a source offset. False when the table can't take it.</summary>
    private bool TableEditAt(int offset, TableOp op)
    {
        if (_doc is null) return false;
        if (TableEditing.Apply(op, _doc.SourceText, offset, _doc.IsMarkdown) is { } e)
        {
            _doc.SourceText = _doc.SourceText.Remove(e.Start, e.Length).Insert(e.Start, e.Text);
            if (_mainVm is not null) _mainVm.StatusText = CompletionController.TableDone(op);
            return true;
        }
        if (_mainVm is not null)
            _mainVm.StatusText = op >= TableOp.MoveRowUp ? Strings.StatusTableCannotMoveOrSort : Strings.StatusEditTableSourceForLayout;
        return false;
    }

    private void CancelBlockEdit()
    {
        var edit = _activeEdit;
        if (edit is null) return;

        edit.Host.RemoveHandler(InputElement.KeyDownEvent, OnBlockEditKeyDown);
        edit.Host.RemoveHandler(InputElement.KeyDownEvent, OnBlockEditArrow);
        edit.Host.LostFocus -= OnBlockEditLostFocus;
        _activeEdit = null;

        edit.Restore();
        UndoTypingDirty(edit);
        // A new paragraph never applied goes again, and the keyboard back to the block above it.
        if (edit.NewParagraph is { } added && _doc is not null && _doc.SourceText == edit.SourceAtOpen)
        {
            _current = Math.Max(0, _current - 1);
            _doc.SourceText = added.Before;
            _doc.IsDirty = added.WasDirty;
        }
        if (_mainVm is not null) _mainVm.StatusText = Strings.StatusEditCancelled;
        if (_rebuildPending && _doc is not null)
            UpdatePreview(_doc);
    }

    /// <summary>True when the block map's source ranges still index the current
    /// text; otherwise reports that the preview is catching up.</summary>
    private bool PanelIsCurrent()
    {
        if (_doc is not null && _doc.SourceText == _panelSource) return true;
        if (_mainVm is not null) _mainVm.StatusText = Strings.StatusPreviewUpdating;
        return false;
    }

    // A block moved or deleted from the keyboard: the keyboard goes to block _current once the page is rendered again.
    private bool _refocusCurrent;

    // Ctrl+/ in an open edit: the edit applied, the Turn into menu once the block is rendered again.
    private bool _turnIntoAfterRender;

    // Ctrl+/: the / key wherever the layout puts it. With Ctrl held, Windows reports no "/" for
    // Shift+7, where German, Swiss, Italian, Spanish and Nordic keyboards have it: Ctrl+Shift+7 too.
    private static bool IsTurnIntoKey(KeyEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
        && (e.Key is Key.Oem2 or Key.Divide || e.KeySymbol == "/"
            || e.Key == Key.D7 && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift));

    private static bool CanTurn(BlockLayout block) =>
        block is ParagraphLayout or HeadingLayout or ListLayout or QuoteLayout or CodeBlockLayout or AdmonitionLayout;

    /// <summary>Ctrl+/: what block <paramref name="i"/> can be turned into, in a menu on it. An open
    /// edit is applied first, and the menu shown once the block is rendered again.</summary>
    private bool ShowTurnIntoMenu(int i)
    {
        if (_doc is null || i < 0 || i >= _blockMap.Count || !CanTurn(_blockMap[i].Block)) return false;
        if (_activeEdit is not null)
        {
            CommitBlockEdit();
            if (_doc.SourceText != _panelSource)
            {
                _refocusCurrent = _turnIntoAfterRender = true;
                return true;
            }
            FocusBlock(i);
        }
        var vis = _blockMap[i].Vis;
        OpenAtBlock(new ContextMenu { ItemsSource = TurnIntoItems(() => _blockMap.FindIndex(b => b.Vis == vis)) }, vis);
        return true;
    }

    private List<Control> TurnIntoItems(Func<int> index)
    {
        var items = new List<Control> { Cmd(Strings.InsertParagraph, () => TurnBlockInto(index(), BlockKind.Paragraph)) };
        for (var level = 1; level <= 6; level++)
        {
            var kind = BlockKind.Heading1 + (level - 1);
            items.Add(Cmd(string.Format(Strings.FormatHeading, level), () => TurnBlockInto(index(), kind)));
        }
        items.Add(Cmd(Strings.InsertBulletedList, () => TurnBlockInto(index(), BlockKind.BulletedList)));
        items.Add(Cmd(Strings.InsertNumberedList, () => TurnBlockInto(index(), BlockKind.NumberedList)));
        items.Add(Cmd(Strings.FormatQuote, () => TurnBlockInto(index(), BlockKind.Quote)));
        items.Add(Cmd(Strings.InsertCodeBlock, () => TurnBlockInto(index(), BlockKind.CodeBlock)));
        if (_doc?.IsMarkdown == false) items.Add(Cmd(Strings.InsertNote, () => TurnBlockInto(index(), BlockKind.Note)));
        return items;
    }

    /// <summary>Block <paramref name="i"/> as another kind, its words kept; a heading turned into its own
    /// level, a paragraph again. The keyboard stays on it. False for a block that can't be.</summary>
    private bool TurnBlockInto(int i, BlockKind kind)
    {
        if (_doc is null || i < 0 || i >= _blockMap.Count || !CanTurn(_blockMap[i].Block) || !PanelIsCurrent()
            || !BlockConversion.Supports(kind, _doc.IsMarkdown))
            return false;
        var block = _blockMap[i].Block;
        if (BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, block.Source, _astToEditor) is not (int start, int length))
        {
            if (_mainVm is not null) _mainVm.StatusText = Strings.StatusBlockFromIncludeCannotModify;
            return false;
        }
        var text = _doc.SourceText;
        if (kind is >= BlockKind.Heading1 and <= BlockKind.Heading6 && block is HeadingLayout
            && HeadingMarker().Match(text.Substring(start, length)).Groups[1].Length == kind - BlockKind.Heading1 + 1)
            kind = BlockKind.Paragraph;
        // A code block's, a quote's or a note's style line ([source,python]) goes with its old kind.
        if (block is CodeBlockLayout or QuoteLayout or AdmonitionLayout && start >= 2)
        {
            var above = text.LastIndexOf('\n', start - 2) + 1;
            var line = text.AsSpan(above, start - 1 - above).Trim();
            if (line.StartsWith("[") && !line.StartsWith("[[") && line.EndsWith("]"))
                (start, length) = (above, length + start - above);
        }
        _current = i;
        _refocusCurrent = true;
        _doc.SourceText = text.Remove(start, length).Insert(start, BlockConversion.TurnInto(text.Substring(start, length), kind, _doc.IsMarkdown));
        return true;
    }

    /// <summary>Block <paramref name="i"/>'s source with the title and attribute lines above it,
    /// or null (said in the status bar) when the page can't change it.</summary>
    private (int Start, int Length)? WholeBlock(int i)
    {
        if (_doc is null || i < 0 || i >= _blockMap.Count || !PanelIsCurrent()) return null;
        var text = _doc.SourceText;
        if (BlockSourceEditing.ResolveEditorSlice(text, _blockMap[i].Block.Source, _astToEditor) is not (int s, int len))
        {
            if (_mainVm is not null) _mainVm.StatusText = Strings.StatusBlockFromIncludeCannotModify;
            return null;
        }
        if (_doc.IsMarkdown) return (s, len); // no such lines; one may be a paragraph of its own
        var above = i > 0 && BlockSourceEditing.ResolveEditorSlice(text, _blockMap[i - 1].Block.Source, _astToEditor) is (int ps, int pl) ? ps + pl : 0;
        return BlockSourceEditing.WithHeader(text, s, len, above);
    }

    // Block i, or the selected blocks when i is the one the keyboard is on.
    private (int First, int Last) BlocksAt(int i) => i == _current && Selection is { } s ? s : (i, i);

    private bool MoveBlock(int i, int step)
    {
        var (first, last) = BlocksAt(i);
        var other = step < 0 ? first - 1 : last + 1;
        if (_doc is null || other < 0 || other >= _blockMap.Count
            || Blocks(first, last) is not { } moved || WholeBlock(other) is not { } passed)
            return false;
        var (a, b) = step < 0 ? (passed, moved) : (moved, passed);
        if (a.Start + a.Length > b.Start) return false; // ranges that overlap can't be swapped
        _current += step;
        if (_selectAnchor >= 0) _selectAnchor += step;
        _doc.SourceText = BlockSourceEditing.SwapSlices(_doc.SourceText, a, b);
        return true;
    }

    private bool DuplicateBlock(int i)
    {
        var (first, last) = BlocksAt(i);
        if (_doc is null || Blocks(first, last) is not { } range) return false;
        var text = _doc.SourceText;
        // Each block of the copy without its ID: the document mustn't hold one twice.
        var copy = _doc.IsMarkdown ? text.Substring(range.Start, range.Length)
            : string.Join("\n\n", Enumerable.Range(first, last - first + 1).Select(k => WholeBlock(k)).OfType<(int Start, int Length)>()
                .Select(b => BlockSourceEditing.WithoutBlockId(text.Substring(b.Start, b.Length))));
        ClearSelection();
        _doc.SourceText = text.Insert(range.Start + range.Length, "\n\n" + copy);
        return true;
    }

    private bool DeleteBlock(int i)
    {
        var (first, last) = BlocksAt(i);
        if (_doc is null || Blocks(first, last) is not { } range) return false;
        _current = Math.Max(0, Math.Min(first, _blockMap.Count - 1 - (last - first + 1))); // the block below, or above the last one
        ClearSelection();
        _doc.SourceText = BlockSourceEditing.DeleteSlice(_doc.SourceText, range.Start, range.Length);
        return true;
    }

    /// <summary>Ctrl+V on a block: the clipboard's text, or an image alone on it, as new blocks after it,
    /// the keyboard on the first. The image is saved beside the document.</summary>
    private bool PasteBlocks(int i)
    {
        if (_doc is null || WholeBlock(i) is not { } after || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return false;
        PasteAfter(_doc, clipboard, after.Start + after.Length);
        return true;
    }

    private async void PasteAfter(DocumentViewModel doc, IClipboard clipboard, int end)
    {
        void Status(string text) { if (_mainVm is not null) _mainVm.StatusText = text; }
        try
        {
            var before = doc.SourceText;
            string? pasted;
            if (await PastedImage.OnlyImageAsync(clipboard))
            {
                if (doc.BaseDirectory is not { Length: > 0 } folder)
                {
                    Status(Strings.StatusSaveBeforePastingImage);
                    return;
                }
                if (await PastedImage.SaveAsync(clipboard, folder, before, doc.IsMarkdown, Status) is not { } target) return;
                pasted = PastedImage.Reference(target, doc.IsMarkdown, ownLine: true);
                Status(string.Format(Strings.StatusImagePasted, target));
            }
            else pasted = (await clipboard.TryGetTextAsync())?.Trim();
            // Changed meanwhile, or another tab: the place found before no longer holds.
            if (string.IsNullOrEmpty(pasted) || doc != _doc || doc.SourceText != before) return;
            _current++;
            _refocusCurrent = true;
            doc.SourceText = BlockInsertion.InsertAfter(before, end, pasted);
        }
        catch (Exception ex)
        {
            Status(string.Format(Strings.StatusImagePasteFailed, ex.Message)); // a clipboard that failed: never the app
        }
    }

    /// <summary>Ctrl+C, Ctrl+X: the source of the selected blocks, or the one the keyboard is on, to the clipboard.</summary>
    private bool CopyBlocks(int i, bool cut)
    {
        var (first, last) = BlocksAt(i);
        if (_doc is null || Blocks(first, last) is not { } range || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return false;
        _ = clipboard.SetTextAsync(_doc.SourceText.Substring(range.Start, range.Length));
        return !cut || DeleteBlock(i);
    }

    private bool NewParagraphAfter(int i)
    {
        if (WholeBlock(i) is not { } a) return false;
        OpenNewParagraph(a.Start + a.Length);
        return true;
    }

    private void ShowBlockContextMenu(Control blockVis, BlockLayout block, Visual? clickTarget, Point pointInScroll)
    {
        if (_doc is null || !PanelIsCurrent()) return;

        var slice = BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, block.Source, _astToEditor);
        if (slice is null)
        {
            if (_mainVm is not null)
                _mainVm.StatusText = Strings.StatusBlockFromIncludeCannotModify;
            return;
        }
        var (start, length) = slice.Value;

        var gesture = Strings.GestureClick;

        var items = new List<Control>();

        if (block is TableLayout table)
        {
            var cellItem = new MenuItem { Header = string.Format(Strings.MenuEditCellContent, gesture) };
            cellItem.Click += (_, _) => EnterCellEdit(blockVis, table, clickTarget, pointInScroll);
            items.Add(cellItem);

            var tableItem = new MenuItem { Header = Strings.MenuEditTableSource };
            tableItem.Click += (_, _) => EnterBlockEdit(blockVis, block);
            items.Add(tableItem);

            // Row/column/merge structure tools — span-aware via the table grid.
            // Guarded: a resolution/menu-build failure must never crash the
            // right-click; it just drops the structural tools and is logged.
            // Markdown tables get rows and columns through the source text.
            try
            {
                if (_doc.IsMarkdown && TryResolveCell(blockVis, table, clickTarget, pointInScroll, out _, out var mr, out var mc)
                    && mr < table.Rows.Count && mc < table.Rows[mr].Cells.Count
                    && CellSlice(table, mr, mc) is (int cellStart, _))
                {
                    items.Add(new Separator());
                    items.Add(new MenuItem { Header = Strings.MenuRow, ItemsSource = new List<Control>
                    {
                        Cmd(Strings.MenuInsertAbove, () => TableEditAt(cellStart, TableOp.RowAbove)),
                        Cmd(Strings.MenuInsertBelow, () => TableEditAt(cellStart, TableOp.RowBelow)),
                        Cmd(Strings.MenuDeleteRow, () => TableEditAt(cellStart, TableOp.DeleteRow)),
                    }.Concat(RowMoves(cellStart)).ToList() });
                    items.Add(new MenuItem { Header = Strings.MenuColumn, ItemsSource = new List<Control>
                    {
                        Cmd(Strings.MenuInsertLeft, () => TableEditAt(cellStart, TableOp.ColumnLeft)),
                        Cmd(Strings.MenuInsertRight, () => TableEditAt(cellStart, TableOp.ColumnRight)),
                        Cmd(Strings.MenuDeleteColumn, () => TableEditAt(cellStart, TableOp.DeleteColumn)),
                    }.Concat(ColumnMoves(cellStart)).ToList() });
                }
                else if (!_doc.IsMarkdown && TryResolveCell(blockVis, table, clickTarget, pointInScroll, out _, out var r, out var c)
                    && r < table.Rows.Count && c < table.Rows[r].Cells.Count)
                {
                    items.Add(new Separator());

                    // Moving and sorting go by the cell's place in the source, as in Markdown.
                    var at = CellSlice(table, r, c)?.Start;
                    var rowMenu = new MenuItem { Header = Strings.MenuRow };
                    rowMenu.ItemsSource = new List<Control>
                    {
                        Cmd(Strings.MenuInsertAbove, () => GridTableOp(table, g => g.InsertRow(r, below: false), 0, Strings.StatusRowInserted)),
                        Cmd(Strings.MenuInsertBelow, () => GridTableOp(table, g => g.InsertRow(r, below: true), 0, Strings.StatusRowInserted)),
                        Cmd(Strings.MenuDeleteRow, () => GridTableOp(table, g => g.DeleteRow(r), 0, Strings.StatusRowDeleted)),
                    }.Concat(at is int a1 ? RowMoves(a1) : []).ToList();
                    items.Add(rowMenu);

                    var colMenu = new MenuItem { Header = Strings.MenuColumn };
                    colMenu.ItemsSource = new List<Control>
                    {
                        Cmd(Strings.MenuInsertLeft, () => GridTableOp(table, g => g.InsertColumn(r, c, right: false), +1, Strings.StatusColumnInserted)),
                        Cmd(Strings.MenuInsertRight, () => GridTableOp(table, g => g.InsertColumn(r, c, right: true), +1, Strings.StatusColumnInserted)),
                        Cmd(Strings.MenuDeleteColumn, () => GridTableOp(table, g => g.DeleteColumn(r, c), -1, Strings.StatusColumnDeleted)),
                    }.Concat(at is int a2 ? ColumnMoves(a2) : []).ToList();
                    items.Add(colMenu);

                    var mergeItems = new List<Control>();
                    if (c + 1 < table.Rows[r].Cells.Count)
                        mergeItems.Add(Cmd(Strings.MenuMergeRight, () => GridTableOp(table, g => g.MergeRight(r, c), 0, Strings.StatusCellsMerged)));
                    if (r + 1 < table.Rows.Count)
                        mergeItems.Add(Cmd(Strings.MenuMergeDown, () => GridTableOp(table, g => g.MergeDown(r, c), 0, Strings.StatusCellsMerged)));
                    var clicked = table.Rows[r].Cells[c];
                    if (clicked.ColSpan > 1 || clicked.RowSpan > 1)
                        mergeItems.Add(Cmd(Strings.MenuSplitCell, () => GridTableOp(table, g => g.Split(r, c), 0, Strings.StatusCellSplit)));
                    if (mergeItems.Count > 0)
                    {
                        var mergeMenu = new MenuItem { Header = Strings.MenuMerge };
                        mergeMenu.ItemsSource = mergeItems;
                        items.Add(mergeMenu);
                    }
                }
            }
            catch (Exception ex)
            {
                _mainVm?.LogError("Table menu build failed", ex);
            }
        }
        else
        {
            var editItem = new MenuItem { Header = string.Format(Strings.MenuEditBlock, gesture) };
            editItem.Click += (_, _) => EnterBlockEdit(blockVis, block);
            items.Add(editItem);
            if (CanTurn(block))
                items.Add(new MenuItem { Header = Strings.MenuTurnInto, InputGesture = new KeyGesture(Key.Oem2, KeyModifiers.Control), ItemsSource = TurnIntoItems(Index) });
        }

        items.Add(new Separator());

        // Found again on click: a render meanwhile may have moved the block.
        int Index() => _blockMap.FindIndex(b => b.Vis == blockVis);
        items.Add(Cmd(Strings.MenuDuplicateBlock, () => DuplicateBlock(Index())));
        items.Add(Cmd(Strings.MenuDeleteBlock, () => DeleteBlock(Index())));

        items.Add(new Separator());
        var markdown = _doc.IsMarkdown;
        var top = WholeBlock(Index())?.Start ?? start; // above its title and attribute lines too
        items.Add(BuildInsertSubmenu(Strings.MenuInsertAbove, markdown,
            snippet => { if (_doc is not null) _doc.SourceText = BlockInsertion.InsertBefore(_doc.SourceText, top, snippet); }));
        items.Add(BuildInsertSubmenu(Strings.MenuInsertBelow, markdown,
            snippet => { if (_doc is not null) _doc.SourceText = BlockInsertion.InsertAfter(_doc.SourceText, start + length, snippet); }));

        var menu = new ContextMenu { ItemsSource = items };
        if (clickTarget is null) OpenAtBlock(menu, blockVis); // from the keyboard
        else menu.Open(blockVis);
    }

    /// <summary>A menu opened from the keyboard: just below the block's first line, not at the pointer.</summary>
    private static void OpenAtBlock(ContextMenu menu, Control vis)
    {
        menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        menu.PlacementRect = new Rect(0, 0, 1, Math.Min(vis.Bounds.Height, 24));
        menu.Open(vis);
    }

    /// <summary>
    /// Context menu for a right-click that didn't land on a block — a gap
    /// between blocks, the margin, or an empty document. Inserts the chosen
    /// block AT THE CLICK LOCATION (after the nearest block above the click,
    /// or before the first block when the click is above everything, or as
    /// the document's only block when empty) so the new content appears where
    /// the user clicked rather than scrolling off the end.
    /// </summary>
    private void ShowInsertMenuAt(Point pointInScroll)
    {
        if (_doc is null || !PanelIsCurrent()) return;
        var contentY = pointInScroll.Y + PreviewScroll.Offset.Y;

        BlockLayout? above = null;
        double aboveTop = double.NegativeInfinity;
        BlockLayout? first = null;
        double firstTop = double.PositiveInfinity;

        foreach (var (vis, block) in _blockMap)
        {
            if (vis.TranslatePoint(default, PreviewScroll) is not Point tp) continue;
            var top = tp.Y + PreviewScroll.Offset.Y;
            if (top < firstTop) { firstTop = top; first = block; }
            if (top <= contentY && top > aboveTop) { aboveTop = top; above = block; }
        }

        // Pick the splice action by location, preferring "after the block
        // above the click". Falls back through "before the first block" to
        // "append" so an include-only or empty region still works.
        Action<string> insert = snippet =>
        {
            if (_doc is null) return;
            _doc.SourceText = BlockInsertion.Append(_doc.SourceText, snippet);
        };

        if (above is not null
            && BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, above.Source, _astToEditor) is (int s, int len))
        {
            var end = s + len;
            insert = snippet =>
            {
                if (_doc is not null)
                    _doc.SourceText = BlockInsertion.InsertAfter(_doc.SourceText, end, snippet);
            };
        }
        else if (first is not null
            && BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, first.Source, _astToEditor) is (int fs, _))
        {
            insert = snippet =>
            {
                if (_doc is not null)
                    _doc.SourceText = BlockInsertion.InsertBefore(_doc.SourceText, fs, snippet);
            };
        }

        var menu = new ContextMenu { ItemsSource = BuildInsertLeaves(insert, _doc.IsMarkdown) };
        menu.Open(this);
    }

    /// <summary>A menu item that runs <paramref name="action"/> on click.</summary>
    // A table's rows and columns rearranged from the cell at source offset `at`.
    private List<Control> RowMoves(int at) =>
    [
        new Separator(),
        Cmd(Strings.MenuMoveUp, () => TableEditAt(at, TableOp.MoveRowUp)),
        Cmd(Strings.MenuMoveDown, () => TableEditAt(at, TableOp.MoveRowDown)),
    ];

    private List<Control> ColumnMoves(int at) =>
    [
        new Separator(),
        Cmd(Strings.MenuMoveLeft, () => TableEditAt(at, TableOp.MoveColumnLeft)),
        Cmd(Strings.MenuMoveRight, () => TableEditAt(at, TableOp.MoveColumnRight)),
        new Separator(),
        Cmd(Strings.MenuSortAscending, () => TableEditAt(at, TableOp.SortAscending)),
        Cmd(Strings.MenuSortDescending, () => TableEditAt(at, TableOp.SortDescending)),
    ];

    private static MenuItem Cmd(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>An "Insert …" submenu whose leaves splice a block snippet via
    /// <paramref name="insert"/>.</summary>
    private static MenuItem BuildInsertSubmenu(string header, bool markdown, Action<string> insert) =>
        new() { Header = header, ItemsSource = BuildInsertLeaves(insert, markdown) };

    /// <summary>The shared list of block-type leaf items.</summary>
    private static List<Control> BuildInsertLeaves(Action<string> insert, bool markdown)
    {
        MenuItem Leaf(string label, string snippet)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => insert(snippet);
            return item;
        }

        if (markdown)
            return
            [
                Leaf(Strings.InsertParagraph, BlockSnippets.Paragraph),
                Leaf(Strings.InsertHeadingH2, MarkdownBlockSnippets.Heading(1)),
                Leaf(Strings.InsertHeadingH3, MarkdownBlockSnippets.Heading(2)),
                Leaf(Strings.InsertBulletedList, MarkdownBlockSnippets.BulletList),
                Leaf(Strings.InsertNumberedList, MarkdownBlockSnippets.NumberedList),
                new Separator(),
                Leaf(Strings.InsertTable, MarkdownBlockSnippets.Table),
                Leaf(Strings.InsertCodeBlock, MarkdownBlockSnippets.CodeBlock),
                Leaf(Strings.InsertHorizontalRule, MarkdownBlockSnippets.ThematicBreak),
            ];
        return
        [
            Leaf(Strings.InsertParagraph, BlockSnippets.Paragraph),
            Leaf(Strings.InsertHeadingH2, BlockSnippets.Heading(1)),
            Leaf(Strings.InsertHeadingH3, BlockSnippets.Heading(2)),
            Leaf(Strings.InsertBulletedList, BlockSnippets.BulletList),
            Leaf(Strings.InsertNumberedList, BlockSnippets.NumberedList),
            new Separator(),
            Leaf(Strings.InsertTable, BlockSnippets.Table),
            Leaf(Strings.InsertCodeBlock, BlockSnippets.CodeBlock),
            Leaf(Strings.InsertNote, BlockSnippets.Note),
            Leaf(Strings.InsertHorizontalRule, BlockSnippets.ThematicBreak),
        ];
    }

    // ── Table structure: resolve the clicked cell + row/column ops ───────

    /// <summary>
    /// Resolve the clicked table cell to its (rowIndex, cellIndex) and the
    /// grid child that renders it. By visual ancestry when the click landed on
    /// cell content; geometrically otherwise (the transparent part of a cell
    /// isn't hit-test visible). The cell index is into the row's <em>declared</em>
    /// cells, matching the renderer's row-major child order.
    /// </summary>
    private bool TryResolveCell(Control tableVis, TableLayout table, Visual? clickTarget,
        Point pointInScroll, out Control cellControl, out int rowIndex, out int cellIndex)
    {
        cellControl = null!;
        rowIndex = -1;
        cellIndex = -1;

        var grid = tableVis as Grid ?? FindDescendantGrid(tableVis);
        if (grid is null) return false;

        var cc = FindGridDirectChild(grid, clickTarget);
        if (cc is null && PreviewScroll.TranslatePoint(pointInScroll, grid) is Point gp)
            cc = grid.Children.OfType<Control>().FirstOrDefault(c => c.Bounds.Contains(gp));
        if (cc is null) return false;

        var flat = grid.Children.IndexOf(cc);
        if (flat < 0) return false;

        var r = 0;
        var idx = flat;
        while (r < table.Rows.Count && idx >= table.Rows[r].Cells.Count)
        {
            idx -= table.Rows[r].Cells.Count;
            r++;
        }
        if (r >= table.Rows.Count) return false;

        cellControl = cc;
        rowIndex = r;
        cellIndex = idx;
        return true;
    }

    /// <summary>Mutate the document's source as a list of physical lines.</summary>
    private void EditSourceLines(Action<List<string>> mutate)
    {
        if (_doc is null) return;
        var lines = _doc.SourceText.Split('\n').ToList();
        mutate(lines);
        _doc.SourceText = string.Join("\n", lines);
    }

    /// <summary>
    /// Apply a structural edit to a table through the span-aware
    /// <see cref="TableGrid"/>: locate the table's body lines, parse them, run
    /// <paramref name="op"/>, and splice the re-serialised body back, adjusting
    /// the <c>cols=</c> attribute by <paramref name="colDelta"/>. Falls back with
    /// a status message when the table can't be modelled (multi-line cells,
    /// multiplier specs), and is a silent no-op when the op doesn't apply (e.g.
    /// no neighbour to merge).
    /// </summary>
    private void GridTableOp(TableLayout table, Func<TableGrid, bool> op, int colDelta, string okStatus)
    {
        if (!PanelIsCurrent()) return;
        var startLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, table.Source.Start.Line - 1, fallback: -1);
        var endLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, table.Source.End.Line - 1, fallback: -1);
        if (startLine < 0 || endLine < startLine)
        {
            if (_mainVm is not null) _mainVm.StatusText = Strings.StatusTableNotEditable;
            return;
        }

        var colsLine = FindColsLine(table);
        string? status = null;
        try
        {
            EditSourceLines(lines =>
            {
                if (endLine >= lines.Count) return;

                int open = -1, close = -1;
                for (var i = startLine; i <= endLine && i < lines.Count; i++)
                    if (AsciiDocContextAnalyzer.IsTableFence(lines[i].Trim()))
                    {
                        if (open < 0) open = i;
                        close = i;
                    }
                if (open < 0 || close <= open) return;

                var body = lines.GetRange(open + 1, close - open - 1);
                // The renderer's row 0 knows the real width (cols= included).
                int? width = table.Rows.Count > 0 ? table.Rows[0].Cells.Sum(c => c.ColSpan) : null;
                var grid = TableGrid.Parse(body, width);
                if (grid is null)
                {
                    status = Strings.StatusEditTableSourceForLayout;
                    return;
                }
                if (!op(grid)) return;

                var newBody = grid.ToLines();
                lines.RemoveRange(open + 1, close - open - 1);
                lines.InsertRange(open + 1, newBody);

                if (colDelta != 0 && colsLine is int cl && cl < lines.Count
                    && TableStructure.AdjustColsAttribute(lines[cl], colDelta) is string adj)
                    lines[cl] = adj;

                status = okStatus;
            });
        }
        catch (Exception ex)
        {
            _mainVm?.LogError("Table edit failed", ex);
            status = string.Format(Strings.StatusTableEditFailed, ex.GetType().Name);
        }

        if (status is not null && _mainVm is not null) _mainVm.StatusText = status;
    }

    /// <summary>Editor line of the table's <c>[cols=…]</c> attribute, scanning
    /// the few block-attribute/title lines just above the <c>|===</c> opener,
    /// or null when the table has no cols spec.</summary>
    private int? FindColsLine(TableLayout table)
    {
        if (_doc is null) return null;
        var tableStart = IncludeExpansionMap.AstLineToEditor(_astToEditor, table.Source.Start.Line - 1, fallback: -1);
        if (tableStart < 0) return null;

        var lines = _doc.SourceText.Split('\n');
        for (var i = tableStart - 1; i >= 0 && i >= tableStart - 3; i--)
        {
            var ln = lines[i].Trim();
            if (ln.StartsWith("[cols=", StringComparison.OrdinalIgnoreCase)) return i;
            if (ln.Length == 0) break;                       // blank line → previous block
            if (!ln.StartsWith('[') && !ln.StartsWith('.')) break; // not an attribute/title
        }
        return null;
    }

    /// <summary>Direct child of <paramref name="grid"/> on the visual
    /// ancestry path of <paramref name="node"/>, or null.</summary>
    private static Control? FindGridDirectChild(Grid grid, Visual? node)
    {
        var cur = node;
        while (cur is not null)
        {
            var parent = cur.GetVisualParent();
            if (ReferenceEquals(parent, grid) && cur is Control c) return c;
            cur = parent;
        }
        return null;
    }

    /// <summary>True when the visual sits inside a ScrollBar (so the press
    /// is a scroll interaction, not a content click).</summary>
    private static bool IsInScrollBar(Visual? node)
    {
        var cur = node;
        while (cur is not null)
        {
            if (cur is ScrollBar) return true;
            cur = cur.GetVisualParent();
        }
        return false;
    }

    private sealed class BlockEditState
    {
        public required TextEditor Editor { get; init; }
        public required Control Host { get; init; }
        public required Action Restore { get; init; }
        public required int Start { get; init; }
        public required int Length { get; init; }
        public required string OriginalSlice { get; init; }
        public required bool WasDirty { get; init; }
        public required string SourceAtOpen { get; init; }
        /// <summary>The open table cell, by row and declared cell; null for a whole block.</summary>
        public (int Row, int Cell)? Cell { get; init; }
        /// <summary>For a new paragraph: the text before it went in, and whether the document had changes.</summary>
        public (string Before, bool WasDirty)? NewParagraph { get; init; }

        /// <summary>Authoring-assist controller for the inline editor, so the
        /// WYSIWYG block editor gets the same completion/snippets/smart-typing
        /// as the source pane.</summary>
        public CompletionController? Assist { get; set; }
    }

    /// <summary>Walk up from <paramref name="from"/> to the nearest
    /// <see cref="TextBlock"/> ancestor (inclusive), or null.</summary>
    private static TextBlock? FindAncestorTextBlock(Visual? from)
    {
        var cur = from;
        while (cur is not null)
        {
            if (cur is TextBlock tb) return tb;
            cur = cur.GetVisualParent();
        }
        return null;
    }

    /// <summary>
    /// Depth-first walk of an inline collection accumulating each
    /// <see cref="Run"/>'s character length to locate the inline whose text
    /// range contains <paramref name="target"/>, returning the most-specific
    /// non-None <see cref="AvaloniaRenderer.SourceRangeProperty"/> on the path
    /// to it (leaf Run's range, else nearest ancestor Span's). Ranges are
    /// absolute since AdocNet 1.0.10.
    /// </summary>
    internal static bool TryFindInlineRange(
        IEnumerable<Inline> inlines, ref int pos, int target, SourceRange inherited, out SourceRange result)
    {
        foreach (var inline in inlines)
        {
            var here = AvaloniaRenderer.GetSourceRange(inline);
            var effective = here.IsNone ? inherited : here;

            switch (inline)
            {
                case Run run:
                    var len = run.Text?.Length ?? 0;
                    if (target >= pos && target < pos + len)
                    {
                        result = effective;
                        return true;
                    }
                    pos += len;
                    break;

                case Span span:
                    if (TryFindInlineRange(span.Inlines, ref pos, target, effective, out result))
                        return true;
                    break;

                // The text layout counts these too: a line break as a newline
                // (two characters on Windows), an embedded control (the
                // checklist box) as one.
                case LineBreak:
                    pos += Environment.NewLine.Length;
                    break;

                case InlineUIContainer:
                    pos += 1;
                    break;
            }
        }
        result = SourceRange.None;
        return false;
    }

    /// <summary>True if <paramref name="node"/> is <paramref name="ancestor"/>
    /// or a visual descendant of it.</summary>
    private static bool IsAncestorOrSelf(Visual ancestor, Visual? node)
    {
        var cur = node;
        while (cur is not null)
        {
            if (ReferenceEquals(cur, ancestor)) return true;
            cur = cur.GetVisualParent();
        }
        return false;
    }

    /// <summary>
    /// Walk up from <paramref name="node"/> to the direct child of
    /// <paramref name="grid"/> on its ancestry path and return that child's
    /// <c>Grid.Row</c>, or -1 if the node isn't inside the grid.
    /// </summary>
    private static int FindGridRowOf(Grid grid, Visual? node)
    {
        var cur = node;
        while (cur is not null)
        {
            var parent = cur.GetVisualParent();
            if (ReferenceEquals(parent, grid) && cur is Control c)
                return Grid.GetRow(c);
            cur = parent;
        }
        return -1;
    }

    /// <summary>
    /// Tells the preview to scroll so that the given source line is at the
    /// top. When a (line → Y) marker map is available from the last render,
    /// uses linear interpolation between markers; otherwise falls back to a
    /// ratio-based mapping over total source lines. Clamped to the preview's
    /// scroll range, then pulled <paramref name="toEnd"/> of the way to its end
    /// (see <see cref="ScrollSyncCalculator.EaseToEnd"/>).
    /// </summary>
    public void SyncToSourceLine(int sourceLine, double fraction, int totalSourceLines, double toEnd = 0)
    {
        _lastSyncLine = sourceLine;
        _lastSyncFrac = fraction;
        _lastSyncTotal = totalSourceLines;
        _lastSyncToEnd = toEnd;

        if (totalSourceLines <= 1) return;

        var max = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
        var y = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine, fraction, _lineMap, PreviewScroll.Extent.Height, PreviewScroll.Viewport.Height,
            totalSourceLines);
        PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, y + (max - y) * toEnd);
    }

    /// <summary>
    /// Scrolls so the given 0-based source line is at the top, without
    /// touching the Split sync state (a later re-render must not snap back).
    /// Without a line map (Page Preview) the position is proportional.
    /// </summary>
    public void ScrollToSourceLine(int sourceLine, int totalSourceLines) =>
        PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine, 0, _lineMap, PreviewScroll.Extent.Height, PreviewScroll.Viewport.Height, totalSourceLines));

    /// <summary>
    /// Walk the visual subtree and return the maximum Y-coordinate of any
    /// painted descendant, in <paramref name="root"/>'s coordinate space.
    /// Uses TranslatePoint per descendant to get correct nested coords.
    /// </summary>
    private static double FindDeepestBottom(Control root)
    {
        double max = root.Bounds.Height;
        var stack = new Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            if (v is Control c)
            {
                var bl = c.TranslatePoint(new Point(0, c.Bounds.Height), root);
                if (bl is not null && bl.Value.Y > max) max = bl.Value.Y;
            }
            foreach (var child in v.GetVisualChildren())
                stack.Push(child);
        }
        return max;
    }

    private static void InvalidateMeasureAll(Control root)
    {
        var stack = new Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            if (v is Layoutable layoutable)
                layoutable.InvalidateMeasure();
            foreach (var c in v.GetVisualChildren())
                stack.Push(c);
        }
    }

    /// <summary>
    /// Build the (editorLine → contentY) map from the authoritative
    /// block-to-visual map. Each block's <see cref="BlockLayout.Source"/> is
    /// the absolute document range, and the visual was rendered 1:1 for that
    /// block, so there's no positional drift. For tables we additionally drill
    /// into the rendered Grid and emit one marker per row using
    /// <see cref="TableRowLayout.Source"/> (also absolute), giving row-level
    /// precision. AST lines are post-include-expansion, translated to editor
    /// lines via <see cref="IncludeExpansionMap"/>; anything from an included
    /// file (editorLine &lt; 0) is skipped.
    /// </summary>
    private SortedList<int, double>? BuildLineMap()
    {
        // AST→editor line map projected from the render's authoritative line
        // origins (AdocNet 1.0.13, #46) — same parse that produced _blockMap.
        var astToEditor = IncludeExpansionMap.FromLineOrigins(_doc?.LineOrigins);
        _astToEditor = astToEditor;

        var map = new SortedList<int, double>();

        foreach (var (vis, block) in _blockMap)
        {
            AddAbsMarker(map, block.Source, vis, astToEditor);

            if (block is TableLayout table)
                AddTableRowMarkers(map, vis, table, astToEditor);
        }

        return map.Count > 0 ? map : null;
    }

    /// <summary>A control's Y in the scrolled content. Measured from the content itself, not
    /// the viewport plus the offset: a map built just after the offset changed, before the
    /// content was arranged for it, would be off by that change.</summary>
    private double? ContentY(Visual vis) => vis.TranslatePoint(default, PreviewHost)?.Y + PreviewHost.Margin.Top;

    /// <summary>Record (editorLine → control's content-space Y) for an
    /// absolute source range, keeping the topmost Y per line.</summary>
    private void AddAbsMarker(SortedList<int, double> map, SourceRange src, Control vis, int[] astToEditor)
    {
        if (src.IsNone) return;
        var editorLine = IncludeExpansionMap.AstLineToEditor(astToEditor, src.Start.Line - 1, fallback: -1);
        if (editorLine < 0) return;
        if (ContentY(vis) is not { } y) return;
        if (!map.TryGetValue(editorLine, out var existing)) map.Add(editorLine, y);
        else if (y < existing) map[editorLine] = y;
    }

    /// <summary>
    /// Drill into a table's rendered Grid and emit one (editorLine → rowY)
    /// marker per row, pairing Grid rows (by attached Grid.Row) with the
    /// TableLayout's rows in order. Each <see cref="TableRowLayout.Source"/>
    /// is absolute.
    /// </summary>
    private void AddTableRowMarkers(SortedList<int, double> map, Control tableVis, TableLayout table, int[] astToEditor)
    {
        var grid = tableVis as Grid ?? FindDescendantGrid(tableVis);
        if (grid is null) return;

        // Min Y per Grid row index.
        var rowY = new Dictionary<int, double>();
        foreach (var child in grid.Children.OfType<Control>())
        {
            var r = Grid.GetRow(child);
            if (r < 0) continue;
            if (ContentY(child) is not { } y) continue;
            if (!rowY.TryGetValue(r, out var ey) || y < ey) rowY[r] = y;
        }

        for (int r = 0; r < table.Rows.Count; r++)
        {
            if (!rowY.TryGetValue(r, out var y)) continue;
            var src = table.Rows[r].Source;
            if (src.IsNone) continue;
            var editorLine = IncludeExpansionMap.AstLineToEditor(astToEditor, src.Start.Line - 1, fallback: -1);
            if (editorLine < 0) continue;
            if (!map.TryGetValue(editorLine, out var existing)) map.Add(editorLine, y);
            else if (y < existing) map[editorLine] = y;
        }
    }

    /// <summary>First <see cref="Grid"/> in the visual subtree, or null.</summary>
    private static Grid? FindDescendantGrid(Visual root)
    {
        var stack = new Stack<Visual>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            if (v is Grid g) return g;
            foreach (var c in v.GetVisualChildren()) stack.Push(c);
        }
        return null;
    }
}
