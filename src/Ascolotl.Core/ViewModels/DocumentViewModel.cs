using AdocNet.Ast;
using CommunityToolkit.Mvvm.ComponentModel;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;

namespace Ascolotl.Core.ViewModels;

public partial class DocumentViewModel : ObservableObject
{
    private static int _untitledCounter;

    private bool _suppressDirtyTracking;

    public string DocumentId { get; init; } = Guid.NewGuid().ToString("N");

    public DateTime? LastKnownWriteTime { get; set; }

    /// <summary>0-based source line at the top of the view, kept per tab and across sessions.</summary>
    public int TopLine { get; set; }

    /// <summary>In Page Preview, how far down the pages the view is: 0 (top) to 1. Pages
    /// have no line map, so this is the tab's position there.</summary>
    public double PageScroll { get; set; }

    /// <summary>The view this tab shows: each tab keeps its own.</summary>
    public EditorMode EditorMode { get; set; } = EditorMode.Wysiwyg;

    /// <summary>Encoding the file was read with, reused on save; null = UTF-8 without BOM.</summary>
    public System.Text.Encoding? FileEncoding { get; set; }

    private bool _isRecovered;
    public bool IsRecovered
    {
        get => _isRecovered;
        set
        {
            if (_isRecovered == value) return;
            _isRecovered = value;
            OnPropertyChanged(nameof(TabTitle));
        }
    }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    /// <summary>One-line description of the AsciiDoc syntax at the caret, shown
    /// in the status bar by the authoring-assist layer. Empty when there's
    /// nothing useful to say (ordinary prose).</summary>
    [ObservableProperty]
    private string _syntaxHint = string.Empty;

    public bool HasSyntaxHint => !string.IsNullOrEmpty(SyntaxHint);

    partial void OnSyntaxHintChanged(string value) => OnPropertyChanged(nameof(HasSyntaxHint));

    [ObservableProperty]
    private string? _filePath;

    /// <summary>Markdown rather than AsciiDoc: follows the file's extension, so
    /// saving as .md switches it. Untitled documents are AsciiDoc.</summary>
    public bool IsMarkdown => MarkdownFile.Is(FilePath);

    /// <summary>
    /// Private folder holding an unsaved import's images. Relative image paths
    /// resolve here until the first save copies them beside the file.
    /// ponytail: not kept in recovery snapshots, so an unsaved import restored
    /// after a crash shows its text without images; store it there if that bites.
    /// </summary>
    public string? AssetDirectory { get; set; }

    /// <summary>Folder that relative includes and images resolve against, or null.</summary>
    public string? BaseDirectory => FilePath is not null ? Path.GetDirectoryName(FilePath) : AssetDirectory;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private int _zoomLevel = 100;

    // --- Render state ---

    [ObservableProperty]
    private DocumentNode? _renderedDocument;

    /// <summary>Per-line origin map from the last successful render (AdocNet
    /// #46) — translates AST (post-include) lines back to editor lines for
    /// scroll-sync, click-to-source, and in-place editing. Set together with
    /// <see cref="RenderedDocument"/>.</summary>
    public IReadOnlyList<AdocNet.LineOrigin>? LineOrigins { get; set; }

    /// <summary>The source text <see cref="RenderedDocument"/> was parsed from;
    /// its source ranges index this text, not a newer <see cref="SourceText"/>.
    /// Set together with <see cref="RenderedDocument"/>.</summary>
    public string? RenderedSource { get; set; }

    /// <summary>Parser diagnostics from the last render, mapped to editor
    /// coordinates for inline display and the status-bar count.</summary>
    [ObservableProperty]
    private IReadOnlyList<DocumentDiagnostic> _diagnostics = [];

    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
    public bool HasErrors => ErrorCount > 0;
    public bool HasWarnings => WarningCount > 0;
    public bool HasDiagnostics => Diagnostics.Count > 0;

    /// <summary>Human-readable list of all diagnostics, for tooltips. Each
    /// carries its actionable <see cref="DocumentDiagnostic.Hint"/> when one is
    /// available, indented on the next line.</summary>
    public string DiagnosticsSummary => string.Join("\n", Diagnostics.Select(d =>
    {
        string head;
        if (!d.IsUnanchored)
            head = string.Format(Strings.DiagnosticAtLine, d.Line + 1, d.Message);
        else if (d.SourceFile is { } file)
            head = $"{file}:{d.SourceFileLine}: {d.Message}"; // inside an include
        else
            head = d.Message;
        return d.Hint is { } hint ? $"{head}\n    → {hint}" : head;
    }));

    partial void OnDiagnosticsChanged(IReadOnlyList<DocumentDiagnostic> value)
    {
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(DiagnosticsSummary));
    }

    [ObservableProperty]
    private bool _lastRenderSucceeded;

    [ObservableProperty]
    private string? _renderError;

    // --- Cursor position (updated by SourceEditorView) ---

    [ObservableProperty]
    private int _cursorLine = 1;

    [ObservableProperty]
    private int _cursorColumn = 1;

    public const int ZoomMin = 50;
    public const int ZoomMax = 300;
    public const int ZoomStep = 10;
    public const double BaseFontSize = 14.0;

    public double FontSize => BaseFontSize * ZoomLevel / 100.0;

    public DocumentState State => IsDirty
        ? DocumentState.Dirty
        : FilePath is null ? DocumentState.New : DocumentState.Clean;

    public string TabTitle
    {
        get
        {
            var name = DisplayName;
            if (IsRecovered) name = string.Format(Strings.TabRecovered, name);
            return IsDirty ? $"{name} *" : name;
        }
    }

    /// <summary>Text typed into a WYSIWYG block that is still open, so not in <see cref="SourceText"/> yet.</summary>
    public sealed record OpenBlockEdit(string Base, int Start, int Length, string Text);

    /// <summary>Kept current by the preview while a block is open for editing.</summary>
    public OpenBlockEdit? UnappliedEdit { get; set; }

    /// <summary>What a crash snapshot keeps: the source with the open block's typing in it.</summary>
    public string RecoveryText => UnappliedEdit is { } e && e.Base == SourceText
        ? SourceText.Remove(e.Start, e.Length).Insert(e.Start, e.Text)
        : SourceText;

    public static DocumentViewModel CreateNew()
    {
        return new DocumentViewModel
        {
            DisplayName = string.Format(Strings.Untitled, ++_untitledCounter),
        };
    }

    public static void ResetUntitledCounter() => _untitledCounter = 0;

    public void BeginLoad() => _suppressDirtyTracking = true;

    public void EndLoad() => _suppressDirtyTracking = false;

    public void MarkClean()
    {
        IsDirty = false;
    }

    partial void OnSourceTextChanged(string value)
    {
        if (!_suppressDirtyTracking)
        {
            IsDirty = true;
        }

        _stats = null;
        OnPropertyChanged(nameof(WordCountText));
        OnPropertyChanged(nameof(StatsTooltip));
    }

    // --- Document stats (status bar) ---

    // Counted once per text change, however many bindings read it.
    private Editing.DocumentStatsResult? _stats;
    private Editing.DocumentStatsResult Stats => _stats ??= Editing.DocumentStats.Count(SourceText);

    /// <summary>Compact prose word count for the status bar, e.g. "1,234 words".</summary>
    public string WordCountText
    {
        get
        {
            var stats = Stats;
            return stats.Words == 1 ? Strings.StatsOneWord : string.Format(Strings.StatsWords, stats.Words);
        }
    }

    /// <summary>Reading time + character count, shown on hover to keep the bar quiet.</summary>
    public string StatsTooltip
    {
        get
        {
            var stats = Stats;
            return string.Format(Strings.StatsTooltip, stats.ReadingMinutes, stats.Characters);
        }
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(TabTitle));
        OnPropertyChanged(nameof(State));
    }

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(TabTitle));

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsMarkdown));
    }

    partial void OnZoomLevelChanged(int value) => OnPropertyChanged(nameof(FontSize));
}
