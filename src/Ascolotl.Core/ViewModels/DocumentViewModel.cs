using AdocNet.Ast;
using CommunityToolkit.Mvvm.ComponentModel;
using Ascolotl.Core.Models;

namespace Ascolotl.Core.ViewModels;

public partial class DocumentViewModel : ObservableObject
{
    private static int _untitledCounter;

    private bool _suppressDirtyTracking;

    public string DocumentId { get; init; } = Guid.NewGuid().ToString("N");

    public DateTime? LastKnownWriteTime { get; set; }

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

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private int _zoomLevel = 100;

    // --- Render state ---

    [ObservableProperty]
    private string _renderedHtml = string.Empty;

    [ObservableProperty]
    private DocumentNode? _renderedDocument;

    /// <summary>Per-line origin map from the last successful render (AdocNet
    /// #46) — translates AST (post-include) lines back to editor lines for
    /// scroll-sync, click-to-source, and in-place editing. Set together with
    /// <see cref="RenderedDocument"/>.</summary>
    public IReadOnlyList<AdocNet.LineOrigin>? LineOrigins { get; set; }

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
            head = $"Line {d.Line + 1}: {d.Message}";
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

    [ObservableProperty]
    private bool _isRendering;

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
            if (IsRecovered) name = $"[Recovered] {name}";
            return IsDirty ? $"{name} *" : name;
        }
    }

    public static DocumentViewModel CreateNew()
    {
        return new DocumentViewModel
        {
            DisplayName = $"Untitled {++_untitledCounter}",
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

        OnPropertyChanged(nameof(WordCountText));
        OnPropertyChanged(nameof(StatsTooltip));
    }

    // --- Document stats (status bar) ---

    /// <summary>Compact prose word count for the status bar, e.g. "1,234 words".</summary>
    public string WordCountText
    {
        get
        {
            var stats = Editing.DocumentStats.Count(SourceText);
            return stats.Words == 1 ? "1 word" : $"{stats.Words:N0} words";
        }
    }

    /// <summary>Reading time + character count, shown on hover to keep the bar quiet.</summary>
    public string StatsTooltip
    {
        get
        {
            var stats = Editing.DocumentStats.Count(SourceText);
            return $"≈ {stats.ReadingMinutes} min read · {stats.Characters:N0} characters";
        }
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(TabTitle));
        OnPropertyChanged(nameof(State));
    }

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(TabTitle));

    partial void OnFilePathChanged(string? value) => OnPropertyChanged(nameof(State));

    partial void OnZoomLevelChanged(int value) => OnPropertyChanged(nameof(FontSize));
}
