using System;
using System.ComponentModel;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Avalonia;
using AdocNet.Layout;
using AdocNet.Layout.Builders;
using Ascolotl.App.Views.Assist;
using Ascolotl.App.Services;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Editing.Assist;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace Ascolotl.App.Views;

public partial class PreviewView : UserControl
{
    private DocumentViewModel? _doc;
    private MainWindowViewModel? _mainVm;

    // Last sync position — re-applied after each content update so the preview
    // doesn't jump to the top when a debounced re-render replaces the content.
    private int _lastSyncLine;
    private double _lastSyncFrac;
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
    // directly. We tried IncrementalAvaloniaRenderer for its SectionTag
    // tagging but the visual tree it produces is nested one section deep,
    // which collapses the line map to one marker per top-level AST child —
    // too coarse for clicks inside a long section. Sticking with per-block
    // positional pairing for now; we can revisit incremental rendering as a
    // separate perf-focused change once the basic mapping is solid.
    private static readonly AvaloniaRenderer _avaloniaRenderer = new() { WrapInScrollViewer = false };
    // The renderer's own look, restored when no style is active.
    private static readonly AvaloniaRenderTheme _builtInTheme = _avaloniaRenderer.Theme;
    private static readonly LayoutBuilder _layoutBuilder = new();

    // Page preview: shares the block renderer; A4 geometry mirrors the PDF
    // export's default page setup (595×842 pt) exactly, via points→DIPs.
    private static readonly PagedAvaloniaRenderer _pagedRenderer = new(_avaloniaRenderer);
    private static readonly PageLayoutOptions _pageOptions = PageLayoutOptions.FromPdfPoints(595, 842);
    // Authoritative 1:1 map of each rendered top-level block visual to its
    // BlockLayout (whose .Source is the ABSOLUTE document source range).
    // Built by rendering block-by-block via AvaloniaRenderer.Render(BlockLayout),
    // which guarantees one visual per layout block — no positional drift.
    private List<(Control Vis, BlockLayout Block)> _blockMap = new();
    // Guards a single subscription to the shared renderer's LinkClicked.
    private static bool _linkHandlerWired;

    // In-flight block-WYSIWYG edit: the rendered block visual has been
    // swapped for an in-place source editor. Null when not editing.
    // Cleared (without commit) whenever UpdatePreview rebuilds the panel,
    // because the swap target dies with the old panel.
    private BlockEditState? _activeEdit;

    // When a single click both commits the open edit AND lands on another
    // block, we queue that block here so the click opens it too:
    //   EditorStart — the block's (or, for a table cell, the table's) editor
    //                 start offset, used to re-find the block after re-render.
    //   RowIndex/CellIndex — the clicked cell, or -1/-1 for a whole block.
    // If the commit changed the source it's consumed after the debounced
    // re-render rebuilds the block map; if the commit was a no-op it's opened
    // immediately against the live map.
    private (int EditorStart, int RowIndex, int CellIndex)? _pendingReentry;

    /// <summary>When set, the next block edit selects all its text so the first
    /// keystroke replaces it (used for the fresh "New paragraph." placeholder).</summary>
    private bool _selectAllOnNextEdit;
    // Fit Width: margin each side of the scaled page, the pane width the
    // current render was scaled for, and the resize debounce.
    private const double FitWidthGap = 12;
    private double _renderedViewportWidth;
    private readonly DispatcherTimer _fitWidthTimer;

    public PreviewView()
    {
        InitializeComponent();
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
        _pendingReentry = (0, -1, -1);
        _doc.SourceText = BlockSnippets.Paragraph;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is not null)
            Application.Current.ActualThemeVariantChanged -= OnThemeChanged;
        if (_mainVm is not null)
            _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var window = TopLevel.GetTopLevel(this) as Window;
        _mainVm = window?.DataContext as MainWindowViewModel;
        if (_mainVm is not null)
            _mainVm.PropertyChanged += OnMainVmPropertyChanged;
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
        if (_doc is not null)
            _doc.PropertyChanged -= OnDocPropertyChanged;

        _doc = DataContext as DocumentViewModel;

        if (_doc is not null)
        {
            _doc.PropertyChanged += OnDocPropertyChanged;
            UpdatePreview(_doc);
        }
    }

    private void OnDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_doc is null) return;

        if (e.PropertyName is nameof(DocumentViewModel.RenderedDocument)
            or nameof(DocumentViewModel.RenderError)
            or nameof(DocumentViewModel.LastRenderSucceeded)
            or nameof(DocumentViewModel.ZoomLevel))
        {
            UpdatePreview(_doc);
        }
    }

    private void UpdatePreview(DocumentViewModel doc)
    {
        if (!doc.LastRenderSucceeded && doc.RenderError is not null)
        {
            PreviewHost.Content = null;
            StatusOverlay.IsVisible = true;
            StatusText.Text = doc.RenderError;
            return;
        }

        StatusOverlay.IsVisible = false;

        if (doc.RenderedDocument is null)
        {
            PreviewHost.Content = null;
            return;
        }

        try
        {
            // Any in-flight block edit dies with the old panel — its swap
            // target is about to be discarded. Handlers on the dying inline
            // editor no-op once the state is cleared.
            _activeEdit = null;

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
            var docDir = doc.FilePath is not null ? Path.GetDirectoryName(doc.FilePath)! : Directory.GetCurrentDirectory();
            var style = PreviewStyle.Load(_mainVm?.StylePath, doc.RenderedDocument.Attributes, docDir);
            _avaloniaRenderer.Theme = style is null ? _builtInTheme : PreviewStyle.ThemeFor(style);
            // Print-like page preview: flow the blocks into fixed A4 pages
            // (mirroring the PDF export's default geometry) via AdocNet
            // 1.0.22's PagedAvaloniaRenderer. This is a *reading* mode — the
            // block map and line map aren't built, so WYSIWYG gestures are
            // inert (guarded at the pointer handler) and scroll-sync degrades
            // to its ratio fallback. The title always shows, matching what
            // the exported PDF prints.
            if (_mainVm?.PagePreview == true)
            {
                RenderPagePreview(layout);
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
            if (showTitleAndToc && !string.IsNullOrWhiteSpace(layout.Title))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = layout.Title,
                    FontWeight = FontWeight.Bold,
                    FontSize = _avaloniaRenderer.Theme.DocumentTitleFontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12),
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
                TextElement.SetForeground(content, Brushes.Black);
                TextElement.SetFontSize(content, style.FontSize * dip);
            }
            // Zoom scales the whole preview (page width included), so a
            // larger zoom also uses more of the window's width. Fit Width
            // scales the page to the pane instead, keeping its line breaks.
            var zoom = doc.ZoomLevel / 100.0;
            if (_mainVm?.PreviewFitWidth == true && PreviewScroll.Viewport.Width > 0)
                zoom = Math.Clamp((PreviewScroll.Viewport.Width - 2 * FitWidthGap) / content.MaxWidth, 0.25, 4);
            _renderedViewportWidth = PreviewScroll.Viewport.Width;
            PreviewHost.Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(zoom, zoom),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = content,
            };
            _renderedRoot = panel;

            InvalidateMeasureAll(content);
            content.UpdateLayout();

            // Build the (sourceLine → Y) map once the visual tree has been laid
            // out. LayoutUpdated fires whenever any descendant is measured/arranged
            // — we attach once and detach inside the handler so it only fires for
            // the next layout pass.
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
                if (_pendingReentry is { } reentry)
                {
                    _pendingReentry = null;
                    Dispatcher.UIThread.Post(() => ConsumeReentry(reentry));
                }

                // Re-apply the last sync position so the preview doesn't jump
                // to the top after each re-render. This is the app driving the
                // preview, NOT a user scroll — suppress reverse-sync so the
                // resulting offset change doesn't bounce back and yank the
                // editor (toward the error line, where a parse error truncates
                // the line-map and any stray sync resolves).
                if (_lastSyncTotal > 0)
                {
                    SuppressReverseSync = true;
                    SyncToSourceLine(_lastSyncLine, _lastSyncFrac, _lastSyncTotal);
                    Dispatcher.UIThread.Post(
                        () => SuppressReverseSync = false, DispatcherPriority.Background);
                }
            };
            PreviewScroll.LayoutUpdated += handler;
        }
        catch (Exception ex)
        {
            PreviewHost.Content = null;
            StatusOverlay.IsVisible = true;
            StatusText.Text = $"Preview render failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Render the document as fixed-size A4 pages (print-like preview). The
    /// interactive state (block map, line map) is cleared — this is a reading
    /// mode — and the scroll offset is preserved across re-renders so typing
    /// in Split mode doesn't snap the pages back to the top.
    /// </summary>
    private void RenderPagePreview(DocumentLayout layout)
    {
        var savedOffset = PreviewScroll.Offset.Y;

        PreviewHost.Content = _pagedRenderer.Render(layout, _pageOptions);
        _renderedRoot = null;
        _blockMap = [];
        _lineMap = null;

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            PreviewScroll.LayoutUpdated -= handler!;
            var max = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
            SuppressReverseSync = true;
            PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, Math.Min(savedOffset, max));
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
        if (_lastSyncTotal <= 1) return;
        // Only respond to a genuine user scroll, i.e. an actual change in the
        // vertical OFFSET. A re-render that grows or shrinks the preview reports
        // an ExtentDelta with no OffsetDelta — reacting to that would reverse-
        // sync the editor on every multi-line insertion/auto-complete, yanking
        // the caret to whatever line the new offset/extent ratio maps to (near a
        // parse error, the truncated line-map resolves that to the error line).
        if (Math.Abs(e.OffsetDelta.Y) < 0.5)
            return;

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            PreviewScroll.Offset.Y, _lineMap, _lastSyncTotal, PreviewScroll.Extent.Height);
        SourceLineRequested?.Invoke(line, frac);
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
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var src = e.Source as Visual;
        var inScrollBar = IsInScrollBar(src);

        // While an in-place edit is open: presses inside the inline editor
        // belong to it, scrollbar presses keep it alive (the user may just
        // be scrolling to check context), and any other press commits (the
        // preview's content is mostly non-focusable, so we can't rely on
        // LostFocus alone to fire for clicks into dead space).
        if (_activeEdit is not null)
        {
            // Presses inside the inline editor belong to it; scrollbar presses
            // keep it alive (the user may just be scrolling for context).
            if (inScrollBar || IsAncestorOrSelf(_activeEdit.Host, src))
                return;

            // Otherwise this press commits. If it's a left-click that lands on
            // ANOTHER block (WYSIWYG), queue that block — or, inside a table,
            // the specific clicked cell — so the same click also opens it.
            _pendingReentry = null;
            if (_mainVm?.EditorMode == EditorMode.Wysiwyg
                && _doc is not null
                && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var cp = e.GetPosition(PreviewScroll);
                if (TryHitBlock(src, cp, out var nextVis, out var nextBlock)
                    && BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, nextBlock.Source, _astToEditor)
                        is (int ns, _))
                {
                    if (nextBlock is TableLayout nextTable
                        && TryResolveCell(nextVis, nextTable, src, cp, out _, out var rr, out var ci))
                        _pendingReentry = (ns, rr, ci);
                    else if (nextBlock is not TableLayout)
                        _pendingReentry = (ns, -1, -1);
                }
            }

            Dispatcher.UIThread.Post(CommitBlockEdit);
            e.Handled = true;
            return;
        }
        if (inScrollBar) return;

        // Page preview is a reading mode: no block editing, no insert menus —
        // the block map isn't built there, so the gestures would misfire.
        if (_mainVm?.PagePreview == true) return;

        // Block-WYSIWYG gestures, resolved with the same _blockMap hit-test
        // as click-to-source tier 2: right-click opens the block context
        // menu; double-click swaps the block (or, inside a table, just the
        // clicked cell's content) for an in-place source editor.
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
        if (e.ClickCount >= 2)
        {
            if (TryHitBlock(src, clickPoint, out var editVis, out var editBlock))
            {
                var entered = editBlock is TableLayout cellTable
                    ? EnterCellEdit(editVis, cellTable, src, clickPoint, RenderedCharOffset(src, e))
                    : EnterBlockEdit(editVis, editBlock, ResolveClickedBlockOffset(src, e));
                if (entered)
                    e.Handled = true;
            }
            return;
        }

        // WYSIWYG single-click: open the clicked block (or cell) in place with
        // the caret at the clicked character — "click and type" without a
        // separate gesture. Split/Source keep single-click for click-to-source
        // (they have a real source pane to navigate), so this is mode-gated.
        if (_mainVm?.EditorMode == EditorMode.Wysiwyg)
        {
            if (TryHitBlock(src, clickPoint, out var wVis, out var wBlock))
            {
                var entered = wBlock is TableLayout wTable
                    ? EnterCellEdit(wVis, wTable, src, clickPoint, RenderedCharOffset(src, e))
                    : EnterBlockEdit(wVis, wBlock, ResolveClickedBlockOffset(src, e));
                if (entered)
                    e.Handled = true;
            }
            return;
        }

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

    // ── Block-WYSIWYG: in-place editing ──────────────────────────────────
    //
    // Modeled on the upstream samples/AdocNet.Avalonia.Editor: a double-
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

        var text = _doc.SourceText;
        var slice = BlockSourceEditing.ResolveEditorSlice(text, block.Source, _astToEditor);
        if (slice is null)
        {
            if (_mainVm is not null)
                _mainVm.StatusText = "This block can't be edited in place (it comes from an include)";
            return false;
        }

        if (_renderedRoot is not Panel panel) return false;
        var index = panel.Children.IndexOf(blockVis);
        if (index < 0) return false;

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
            statusHint: "Editing block — Apply (Ctrl+Enter) or click away to commit, Esc to cancel",
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
    private bool EnterCellEditByIndex(Control tableVis, TableLayout table, int rowIndex, int cellIndex)
    {
        var grid = tableVis as Grid ?? FindDescendantGrid(tableVis);
        if (grid is null) return false;

        var cellsPerRow = table.Rows.Select(r => r.Cells.Count).ToList();
        if (TableStructure.FlatCellIndex(cellsPerRow, rowIndex, cellIndex) is not int flat)
            return false;
        if (flat < 0 || flat >= grid.Children.Count) return false;
        if (grid.Children[flat] is not Control cellControl) return false;
        return EnterCellEditCore(cellControl, table, rowIndex, cellIndex, caretInContent: null);
    }

    private bool EnterCellEditCore(Control cellControl, TableLayout table, int r, int cellIdx,
        int? caretInContent)
    {
        if (_doc is null) return false;

        // Entering an edit supersedes any queued chain-re-entry.
        _pendingReentry = null;

        var cell = table.Rows[r].Cells[cellIdx];

        var text = _doc.SourceText;
        // AdocNet 1.0.13 (#45) exposes a per-cell Source range covering exactly
        // the cell's content (no delimiters, no cell-spec prefix), so we splice
        // it directly — no row-slice scanning needed.
        var cellSlice = BlockSourceEditing.ResolveEditorSlice(text, cell.Source, _astToEditor);
        if (cellSlice is null)
        {
            if (_mainVm is not null)
                _mainVm.StatusText = "This table cell can't be edited in place (it comes from an include)";
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
            statusHint: "Editing cell — Apply (Ctrl+Enter) or click away to commit, Esc to cancel",
            caretInSlice: caretInContent);
        return true;
    }

    /// <summary>Common tail of both edit entry points: record state, place
    /// the caret, wire the commit/cancel handlers, focus the editor.</summary>
    private void BeginEdit(TextEditor editor, Control host, int start, int length,
        string originalSlice, Action restore, string statusHint, int? caretInSlice = null)
    {
        _activeEdit = new BlockEditState
        {
            Editor = editor,
            Host = host,
            Restore = restore,
            Start = start,
            Length = length,
            OriginalSlice = originalSlice,
            // The inline editor holds one block's slice, so feed the whole
            // document for anchor/attribute (document-aware) completion.
            Assist = new CompletionController(editor,
                documentSource: () => _doc?.SourceText ?? string.Empty),
        };

        if (caretInSlice is int c)
            editor.CaretOffset = Math.Clamp(c, 0, length);

        // KeyDown must TUNNEL: AvaloniaEdit's TextArea consumes Escape (and
        // would insert a newline for Enter variants) before a bubbling
        // handler ever sees the key. Tunneling on the host fires first and
        // also covers the Apply/Cancel buttons. LostFocus bubbles, so the
        // host hears it from the TextArea and the buttons alike.
        host.AddHandler(InputElement.KeyDownEvent, OnBlockEditKeyDown, RoutingStrategies.Tunnel);
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
    /// Build the in-place editor: an AvaloniaEdit editor over the slice plus
    /// an Apply/Cancel button bar, wrapped in a highlighted border.
    /// </summary>
    private (TextEditor Editor, Border Host) BuildInlineEditor(string slice, bool compact)
    {
        var editor = new TextEditor
        {
            Document = new TextDocument(slice),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            FontSize = compact ? 12 : 13,
            ShowLineNumbers = false,
            WordWrap = true,
            Background = Brushes.Transparent,
            Padding = compact ? new Thickness(4, 2) : new Thickness(8, 6),
        };

        var applyButton = new Button { Content = "✓ Apply", FontSize = 11, Padding = new Thickness(8, 2) };
        ToolTip.SetTip(applyButton, "Apply (Ctrl+Enter)");
        applyButton.Click += (_, _) => CommitBlockEdit();

        var cancelButton = new Button { Content = "✕ Cancel", FontSize = 11, Padding = new Thickness(8, 2) };
        ToolTip.SetTip(cancelButton, "Cancel (Esc)");
        cancelButton.Click += (_, _) => CancelBlockEdit();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(4, 2, 4, 4),
            Children = { applyButton, cancelButton },
        };

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(editor);

        var host = new Border
        {
            Background = new SolidColorBrush(Color.Parse(IsDarkTheme ? "#2E2A1C" : "#FFFBE6")),
            BorderBrush = new SolidColorBrush(Color.Parse(IsDarkTheme ? "#E8608A" : "#D94F7A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 2),
            Child = dock,
        };
        return (editor, host);
    }

    private void OnBlockEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeEdit is null) return;

        // While a completion popup or snippet session is up, let the assist
        // controller own Esc/Tab/Enter — don't cancel the block edit out from
        // under it. (This host handler tunnels first, so it must defer.)
        if (_activeEdit.Assist is { IsActive: true }) return;

        if (e.Key == Key.Escape)
        {
            CancelBlockEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CommitBlockEdit();
            e.Handled = true;
        }
    }

    private void OnBlockEditLostFocus(object? sender, RoutedEventArgs e)
    {
        // Defer one dispatcher tick so the keystroke/click that moved focus
        // finishes processing before the editor is torn down. After the
        // tick, skip the commit when focus merely moved WITHIN the inline
        // editor host (e.g. onto the Apply/Cancel buttons) — otherwise the
        // deferred commit would fire before the Cancel button's Click and
        // turn a cancel into an apply.
        Dispatcher.UIThread.Post(() =>
        {
            var edit = _activeEdit;
            if (edit is null) return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused is not null && IsAncestorOrSelf(edit.Host, focused)) return;
            CommitBlockEdit();
        });
    }

    private void CommitBlockEdit()
    {
        var edit = _activeEdit;
        if (edit is null || _doc is null) return;

        edit.Host.RemoveHandler(InputElement.KeyDownEvent, OnBlockEditKeyDown);
        edit.Host.LostFocus -= OnBlockEditLostFocus;
        _activeEdit = null;

        var newSlice = edit.Editor.Text;
        var (action, s, len) = BlockSourceEditing.DecideCommit(
            _doc.SourceText, edit.Start, edit.Length, edit.OriginalSlice, newSlice);

        if (action == BlockSourceEditing.CommitAction.Replace)
        {
            // Freeze the inline editor until the debounced re-render replaces
            // the panel — the splice below kicks that off via dirty tracking.
            edit.Editor.IsReadOnly = true;
            _doc.SourceText = _doc.SourceText.Remove(s, len).Insert(s, newSlice);
            if (_mainVm is not null) _mainVm.StatusText = "Block updated";

            // Shift a queued re-entry for the splice: a block above the edit is
            // unchanged; one below moves by the length delta. Consumed once the
            // re-render rebuilds the block map (see UpdatePreview).
            if (_pendingReentry is { } p)
            {
                var shifted = BlockSourceEditing.ShiftOffsetForSplice(p.EditorStart, s, len, newSlice.Length);
                _pendingReentry = (shifted, p.RowIndex, p.CellIndex);
            }
        }
        else
        {
            edit.Restore();
            if (_mainVm is not null)
                _mainVm.StatusText = action == BlockSourceEditing.CommitAction.Abort
                    ? "Edit discarded — the document changed while the block was open"
                    : "Ready";

            // No source change → no re-render to host the re-entry; open the
            // queued target now against the still-current block map.
            if (_pendingReentry is { } p)
            {
                _pendingReentry = null;
                ConsumeReentry(p);
            }
        }
    }

    /// <summary>Open the queued chain-re-entry target: re-find its block by
    /// editor offset, then open the whole block or the specific table cell.</summary>
    private void ConsumeReentry((int EditorStart, int RowIndex, int CellIndex) req)
    {
        var target = FindBlockByEditorStart(req.EditorStart);
        if (target is null) return;

        var (vis, block) = target.Value;
        if (block is TableLayout tbl)
        {
            if (req.CellIndex >= 0)
                EnterCellEditByIndex(vis, tbl, req.RowIndex, req.CellIndex);
        }
        else
        {
            EnterBlockEdit(vis, block);
        }
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

    private void CancelBlockEdit()
    {
        var edit = _activeEdit;
        if (edit is null) return;

        edit.Host.RemoveHandler(InputElement.KeyDownEvent, OnBlockEditKeyDown);
        edit.Host.LostFocus -= OnBlockEditLostFocus;
        _activeEdit = null;

        edit.Restore();
        if (_mainVm is not null) _mainVm.StatusText = "Edit cancelled";
    }

    private void ShowBlockContextMenu(Control blockVis, BlockLayout block, Visual? clickTarget, Point pointInScroll)
    {
        if (_doc is null) return;

        var slice = BlockSourceEditing.ResolveEditorSlice(_doc.SourceText, block.Source, _astToEditor);
        if (slice is null)
        {
            if (_mainVm is not null)
                _mainVm.StatusText = "This block comes from an include and can't be modified here";
            return;
        }
        var (start, length) = slice.Value;

        // In WYSIWYG mode a single click opens the editor; Split keeps the
        // double-click gesture (single click navigates the source pane).
        var gesture = _mainVm?.EditorMode == EditorMode.Wysiwyg ? "click" : "double-click";

        var items = new List<Control>();

        if (block is TableLayout table)
        {
            var cellItem = new MenuItem { Header = $"Edit cell content ({gesture})" };
            cellItem.Click += (_, _) => EnterCellEdit(blockVis, table, clickTarget, pointInScroll);
            items.Add(cellItem);

            var tableItem = new MenuItem { Header = "Edit table source" };
            tableItem.Click += (_, _) => EnterBlockEdit(blockVis, block);
            items.Add(tableItem);

            // Row/column/merge structure tools — span-aware via the table grid.
            // Guarded: a resolution/menu-build failure must never crash the
            // right-click; it just drops the structural tools and is logged.
            try
            {
                if (TryResolveCell(blockVis, table, clickTarget, pointInScroll, out _, out var r, out var c)
                    && r < table.Rows.Count && c < table.Rows[r].Cells.Count)
                {
                    items.Add(new Separator());

                    var rowMenu = new MenuItem { Header = "Row" };
                    rowMenu.ItemsSource = new[]
                    {
                        Cmd("Insert above", () => GridTableOp(table, g => g.InsertRow(r, below: false), 0, "Row inserted")),
                        Cmd("Insert below", () => GridTableOp(table, g => g.InsertRow(r, below: true), 0, "Row inserted")),
                        Cmd("Delete row", () => GridTableOp(table, g => g.DeleteRow(r), 0, "Row deleted")),
                    };
                    items.Add(rowMenu);

                    var colMenu = new MenuItem { Header = "Column" };
                    colMenu.ItemsSource = new[]
                    {
                        Cmd("Insert left", () => GridTableOp(table, g => g.InsertColumn(r, c, right: false), +1, "Column inserted")),
                        Cmd("Insert right", () => GridTableOp(table, g => g.InsertColumn(r, c, right: true), +1, "Column inserted")),
                        Cmd("Delete column", () => GridTableOp(table, g => g.DeleteColumn(r, c), -1, "Column deleted")),
                    };
                    items.Add(colMenu);

                    var mergeItems = new List<Control>();
                    if (c + 1 < table.Rows[r].Cells.Count)
                        mergeItems.Add(Cmd("Merge right", () => GridTableOp(table, g => g.MergeRight(r, c), 0, "Cells merged")));
                    if (r + 1 < table.Rows.Count)
                        mergeItems.Add(Cmd("Merge down", () => GridTableOp(table, g => g.MergeDown(r, c), 0, "Cells merged")));
                    var clicked = table.Rows[r].Cells[c];
                    if (clicked.ColSpan > 1 || clicked.RowSpan > 1)
                        mergeItems.Add(Cmd("Split cell", () => GridTableOp(table, g => g.Split(r, c), 0, "Cell split")));
                    if (mergeItems.Count > 0)
                    {
                        var mergeMenu = new MenuItem { Header = "Merge" };
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
            var editItem = new MenuItem { Header = $"Edit block ({gesture})" };
            editItem.Click += (_, _) => EnterBlockEdit(blockVis, block);
            items.Add(editItem);
        }

        items.Add(new Separator());

        var duplicateItem = new MenuItem { Header = "Duplicate block" };
        duplicateItem.Click += (_, _) =>
        {
            if (_doc is not null)
                _doc.SourceText = BlockSourceEditing.DuplicateSlice(_doc.SourceText, start, length);
        };
        items.Add(duplicateItem);

        var deleteItem = new MenuItem { Header = "Delete block" };
        deleteItem.Click += (_, _) =>
        {
            if (_doc is not null)
                _doc.SourceText = BlockSourceEditing.DeleteSlice(_doc.SourceText, start, length);
        };
        items.Add(deleteItem);

        items.Add(new Separator());
        items.Add(BuildInsertSubmenu("Insert above",
            snippet => { if (_doc is not null) _doc.SourceText = BlockInsertion.InsertBefore(_doc.SourceText, start, snippet); }));
        items.Add(BuildInsertSubmenu("Insert below",
            snippet => { if (_doc is not null) _doc.SourceText = BlockInsertion.InsertAfter(_doc.SourceText, start + length, snippet); }));

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(blockVis);
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
        if (_doc is null) return;
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

        var menu = new ContextMenu { ItemsSource = BuildInsertLeaves(insert) };
        menu.Open(this);
    }

    /// <summary>A menu item that runs <paramref name="action"/> on click.</summary>
    private static MenuItem Cmd(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>An "Insert …" submenu whose leaves splice a block snippet via
    /// <paramref name="insert"/>.</summary>
    private static MenuItem BuildInsertSubmenu(string header, Action<string> insert) =>
        new() { Header = header, ItemsSource = BuildInsertLeaves(insert) };

    /// <summary>The shared list of block-type leaf items.</summary>
    private static List<Control> BuildInsertLeaves(Action<string> insert)
    {
        MenuItem Leaf(string label, string snippet)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => insert(snippet);
            return item;
        }

        return new List<Control>
        {
            Leaf("Paragraph", BlockSnippets.Paragraph),
            Leaf("Heading (H2)", BlockSnippets.Heading(1)),
            Leaf("Heading (H3)", BlockSnippets.Heading(2)),
            Leaf("Bulleted list", BlockSnippets.BulletList),
            Leaf("Numbered list", BlockSnippets.NumberedList),
            new Separator(),
            Leaf("Table", BlockSnippets.Table),
            Leaf("Code block", BlockSnippets.CodeBlock),
            Leaf("Note", BlockSnippets.Note),
            Leaf("Horizontal rule", BlockSnippets.ThematicBreak),
        };
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
        var startLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, table.Source.Start.Line - 1, fallback: -1);
        var endLine = IncludeExpansionMap.AstLineToEditor(_astToEditor, table.Source.End.Line - 1, fallback: -1);
        if (startLine < 0 || endLine < startLine)
        {
            if (_mainVm is not null) _mainVm.StatusText = "This table can't be edited here";
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
                var grid = TableGrid.Parse(body);
                if (grid is null)
                {
                    status = "Edit the table source for this layout";
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
            status = $"Table edit failed: {ex.GetType().Name}";
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
    private static bool TryFindInlineRange(
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
    /// ratio-based mapping over total source lines.
    /// </summary>
    /// <returns>
    /// True when the requested position was reached, false when it would
    /// have required scrolling past the preview's maximum — the caller can
    /// use this signal to clamp the editor's scroll so it can't drift past
    /// the point where the preview is already at its end.
    /// </returns>
    public bool SyncToSourceLine(int sourceLine, double fraction, int totalSourceLines)
    {
        _lastSyncLine = sourceLine;
        _lastSyncFrac = fraction;
        _lastSyncTotal = totalSourceLines;

        if (totalSourceLines <= 1) return true;

        var maxScroll = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
        var rawOffset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine, fraction, _lineMap,
            PreviewScroll.Extent.Height, PreviewScroll.Viewport.Height,
            totalSourceLines);

        var clamped = Math.Min(rawOffset, maxScroll);
        PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, clamped);

        // Compute against the un-clamped target so consumers can detect that
        // the requested position lay past the preview's max. ComputePreview-
        // Offset already clamps internally, but only by maxScroll; so an
        // exact-max value is the saturation signal.
        return rawOffset < maxScroll - 0.5;
    }

    /// <summary>Scroll the preview to a 0–1 ratio of its content height.</summary>
    public void ScrollToRatio(double ratio)
    {
        var clamped = Math.Clamp(ratio, 0.0, 1.0);
        var maxScroll = Math.Max(0, PreviewScroll.Extent.Height - PreviewScroll.Viewport.Height);
        PreviewScroll.Offset = new Vector(PreviewScroll.Offset.X, clamped * maxScroll);
    }

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

    /// <summary>Record (editorLine → control's content-space Y) for an
    /// absolute source range, keeping the topmost Y per line.</summary>
    private void AddAbsMarker(SortedList<int, double> map, SourceRange src, Control vis, int[] astToEditor)
    {
        if (src.IsNone) return;
        var editorLine = IncludeExpansionMap.AstLineToEditor(astToEditor, src.Start.Line - 1, fallback: -1);
        if (editorLine < 0) return;
        var tp = vis.TranslatePoint(default, PreviewScroll);
        if (tp is null) return;
        var y = tp.Value.Y + PreviewScroll.Offset.Y;
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
            var tp = child.TranslatePoint(default, PreviewScroll);
            if (tp is null) continue;
            var y = tp.Value.Y + PreviewScroll.Offset.Y;
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

    private static T? FindDescendantOfType<T>(Visual root) where T : Visual
    {
        foreach (var c in root.GetVisualChildren())
        {
            if (c is T match) return match;
            var found = FindDescendantOfType<T>(c);
            if (found is not null) return found;
        }
        return null;
    }

}
