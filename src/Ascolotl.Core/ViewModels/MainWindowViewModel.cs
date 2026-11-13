using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Core.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly IRenderService _renderService;
    private readonly IExportService? _exportService;
    private readonly IWebsiteExportService? _websiteExportService;
    private readonly IDocxImportService? _docxImportService;
    private readonly IRecoveryStore? _recoveryStore;
    private readonly ISessionStore? _sessionStore;
    private readonly IAppLog? _log;
    private readonly int _debounceMs;

    private const int MaxRecentFiles = 20;
    private const int AutosaveIntervalSeconds = 30;

    private CancellationTokenSource? _renderCts;
    private readonly Stack<string> _closedTabPaths = new();
    private System.Threading.Timer? _autosaveTimer;

    [ObservableProperty]
    private DocumentViewModel? _selectedDocument;

    [ObservableProperty]
    private AppTheme _currentTheme;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private EditorMode _editorMode = EditorMode.Split;

    [ObservableProperty]
    private bool _showTitleAndToc;

    [ObservableProperty]
    private bool _syncScrolling = true;

    [ObservableProperty]
    private bool _showOutlinePane;

    [ObservableProperty]
    private bool _hamburgerMenu = true;

    [ObservableProperty]
    private bool _editorWordWrap = false;

    [ObservableProperty]
    private bool _previewWordWrap = true;

    /// <summary>Print-like page preview: the preview pane flows content into
    /// fixed A4 pages (matching the PDF export's geometry) instead of a
    /// continuous column.</summary>
    [ObservableProperty]
    private bool _pagePreview;

    [RelayCommand]
    private void TogglePagePreview() => PagePreview = !PagePreview;
    /// <summary>Scale the preview page to the pane's width; explicit zoom turns it off.</summary>
    [ObservableProperty]
    private bool _previewFitWidth;

    /// <summary>How PDF exports derive their Title metadata.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPdfTitleFull), nameof(IsPdfTitleUpToColon))]
    private PdfTitleRule _pdfTitleRule;

    public bool IsPdfTitleFull => PdfTitleRule == PdfTitleRule.FullTitle;
    public bool IsPdfTitleUpToColon => PdfTitleRule == PdfTitleRule.UpToFirstColon;

    /// <summary>Show the PDF title for editing before each PDF export.</summary>
    [ObservableProperty]
    private bool _askPdfTitle;

    /// <summary>
    /// The user's default style: an asciidoctor-pdf YAML theme applied to
    /// PDF/HTML/EPUB export and the live preview; null = built-in look.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStyle))]
    [NotifyPropertyChangedFor(nameof(StyleMenuHeader))]
    private string? _stylePath;

    public bool HasStyle => StylePath is not null;

    /// <summary>Style-menu line naming the active style ("_" doubled: it marks access keys).</summary>
    public string StyleMenuHeader =>
        $"Current: {(StylePath is null ? "Built-in" : Path.GetFileName(StylePath).Replace("_", "__"))}";

    public ObservableCollection<OutlineItem> OutlineItems { get; } = [];

    // --- Project workspace (folder sidebar) ---

    /// <summary>Root folder of the open project workspace, or null.</summary>
    [ObservableProperty]
    private string? _projectFolderPath;

    /// <summary>True when the sidebar shows the file tree instead of the outline.</summary>
    [ObservableProperty]
    private bool _sidebarFilesActive;

    public bool HasProject => ProjectFolderPath is not null;

    public ObservableCollection<ProjectNode> ProjectRoots { get; } = [];

    partial void OnProjectFolderPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasProject));
        if (value is null) SidebarFilesActive = false;
    }

    // Computed properties for menu checkmarks
    public bool IsSourceMode => EditorMode == EditorMode.Source;
    public bool IsSplitMode => EditorMode == EditorMode.Split;
    public bool IsWysiwygMode => EditorMode == EditorMode.Wysiwyg;
    public bool IsThemeLight => CurrentTheme == AppTheme.Light;
    public bool IsThemeDark => CurrentTheme == AppTheme.Dark;
    public bool IsThemeSystem => CurrentTheme == AppTheme.System;

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];

    /// <summary>True when no document tabs are open — drives the empty-state view.</summary>
    public bool HasNoDocuments => Documents.Count == 0;

    public CommandPaletteViewModel CommandPalette { get; }

    /// <summary>Searchable AsciiDoc syntax reference (F1) — Coach-style help.</summary>
    public SyntaxReferenceViewModel SyntaxReference { get; } = new();

    [RelayCommand]
    private void ToggleSyntaxReference()
    {
        if (SyntaxReference.IsOpen) SyntaxReference.Close();
        else SyntaxReference.Open();
    }

    public event Action? RecentFilesChanged;
    public event Action<int>? NavigateToLineRequested;

    /// <summary>Raised after a new (empty) document is created and selected, so
    /// the view can give it focus for immediate typing.</summary>
    public event Action? NewDocumentCreated;

    /// <summary>
    /// Awaitable handle to the most recent render operation.
    /// Completes when the render (or its cancellation) finishes.
    /// </summary>
    public Task PendingRenderTask { get; private set; } = Task.CompletedTask;

    public MainWindowViewModel(
        IFileService fileService,
        IDialogService dialogService,
        IRenderService renderService,
        IExportService? exportService = null,
        IRecoveryStore? recoveryStore = null,
        ISessionStore? sessionStore = null,
        IAppLog? log = null,
        int debounceMs = 300,
        IWebsiteExportService? websiteExportService = null,
        IDocxImportService? docxImportService = null)
    {
        _fileService = fileService;
        _dialogService = dialogService;
        _renderService = renderService;
        _exportService = exportService;
        _websiteExportService = websiteExportService;
        _docxImportService = docxImportService;
        _recoveryStore = recoveryStore;
        _sessionStore = sessionStore;
        _log = log;
        _debounceMs = debounceMs;
        CommandPalette = new CommandPaletteViewModel(BuildCommandRegistry());
        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoDocuments));
    }

    public void LoadRecentFiles(IEnumerable<string> paths)
    {
        RecentFiles.Clear();
        foreach (var path in paths)
            RecentFiles.Add(path);
    }

    /// <summary>Record an error (with full stack) to the app log — used by views
    /// to report a caught failure rather than letting it crash the app.</summary>
    public void LogError(string message, Exception ex) => _log?.Error(message, ex);

    // --- Autosave ---

    public void StartAutosave()
    {
        if (_recoveryStore is null) return;
        _autosaveTimer = new System.Threading.Timer(
            _ => RunAutosave(), null,
            TimeSpan.FromSeconds(AutosaveIntervalSeconds),
            TimeSpan.FromSeconds(AutosaveIntervalSeconds));
    }

    public void StopAutosave()
    {
        _autosaveTimer?.Dispose();
        _autosaveTimer = null;
    }

    internal void RunAutosave()
    {
        if (_recoveryStore is null) return;

        foreach (var doc in Documents.ToList())
        {
            if (!doc.IsDirty) continue;

            try
            {
                _recoveryStore.SaveSnapshot(
                    doc.DocumentId, doc.SourceText, doc.FilePath, doc.DisplayName);
            }
            catch (Exception ex)
            {
                _log?.Error($"Autosave failed for {doc.DisplayName}", ex);
            }
        }
    }

    /// <summary>
    /// Emergency save for all dirty documents. Called on unhandled exceptions.
    /// </summary>
    public void EmergencyRecoverySave()
    {
        if (_recoveryStore is null) return;

        foreach (var doc in Documents.ToList())
        {
            if (!doc.IsDirty) continue;

            try
            {
                _recoveryStore.SaveSnapshot(
                    doc.DocumentId, doc.SourceText, doc.FilePath, doc.DisplayName);
            }
            catch
            {
                // Best effort — cannot log if we're already crashing
            }
        }
    }

    // --- Recovery ---

    public async Task<bool> CheckAndRecoverAsync()
    {
        if (_recoveryStore is null) return false;

        var entries = await Task.Run(() => _recoveryStore.GetRecoveryEntries());
        if (entries.Count == 0) return false;

        _log?.Info($"Found {entries.Count} recoverable document(s)");

        var shouldRecover = await _dialogService.ShowRecoveryDialogAsync(entries.Count);
        if (!shouldRecover)
        {
            _recoveryStore.ClearAll();
            _log?.Info("User discarded recovery data");
            return false;
        }

        foreach (var entry in entries)
        {
            var sourceText = _recoveryStore.LoadSnapshot(entry.DocumentId);
            if (sourceText is null) continue;

            var doc = new DocumentViewModel
            {
                DocumentId = entry.DocumentId,
                IsRecovered = true,
            };
            doc.BeginLoad();
            doc.FilePath = entry.OriginalFilePath;
            doc.DisplayName = entry.DisplayName;
            doc.SourceText = sourceText;
            doc.EndLoad();
            doc.IsDirty = true; // Recovered docs are dirty

            // Snapshot the file's current write time so conflict detection
            // works on the first save — catches modifications that happened
            // while the app was down.
            if (entry.OriginalFilePath is not null)
            {
                try { doc.LastKnownWriteTime = File.GetLastWriteTimeUtc(entry.OriginalFilePath); }
                catch { /* file may be gone — conflict check will be skipped */ }
            }

            Documents.Add(doc);
        }

        if (Documents.Count > 0)
            SelectedDocument = Documents[0];

        // Do NOT ClearAll here — recovery data stays on disk as a safety net
        // until each document is explicitly saved (RemoveRecoverySnapshot) or
        // the app shuts down cleanly (CleanupRecoveryOnShutdown).
        _log?.Info($"Recovered {Documents.Count} document(s)");
        StatusText = $"Recovered {Documents.Count} document(s)";
        return true;
    }

    // --- Session ---

    public async Task RestoreSessionAsync()
    {
        if (_sessionStore is null) return;

        var session = _sessionStore.Load();
        if (session is null) return;

        // Read all session files off the UI thread
        var loaded = await Task.Run(() =>
        {
            var results = new List<(SessionDocument Session, string Content, DateTime WriteTime)>();
            foreach (var sessionDoc in session.OpenDocuments)
            {
                if (sessionDoc.FilePath is null) continue;
                try
                {
                    var content = File.ReadAllText(sessionDoc.FilePath);
                    var writeTime = File.GetLastWriteTimeUtc(sessionDoc.FilePath);
                    results.Add((sessionDoc, content, writeTime));
                }
                catch (Exception ex)
                {
                    _log?.Warn($"Could not restore {sessionDoc.DisplayName}: {ex.Message}");
                }
            }
            return results;
        });

        foreach (var (sessionDoc, content, writeTime) in loaded)
        {
            // Skip if already open (e.g. from recovery)
            if (Documents.Any(d => string.Equals(d.FilePath, sessionDoc.FilePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var doc = new DocumentViewModel { DocumentId = sessionDoc.DocumentId };
            doc.BeginLoad();
            doc.FilePath = sessionDoc.FilePath;
            doc.DisplayName = sessionDoc.DisplayName;
            doc.SourceText = content;
            doc.EndLoad();
            doc.MarkClean();
            doc.LastKnownWriteTime = writeTime;

            Documents.Add(doc);
        }

        if (session.SelectedIndex >= 0 && session.SelectedIndex < Documents.Count)
            SelectedDocument = Documents[session.SelectedIndex];
        else if (Documents.Count > 0)
            SelectedDocument = Documents[0];

        if (Documents.Count > 0)
        {
            _log?.Info($"Restored session with {Documents.Count} document(s)");
            StatusText = $"Session restored ({Documents.Count} document(s))";
        }
    }

    public void SaveSession()
    {
        if (_sessionStore is null) return;

        var state = new SessionState
        {
            SelectedIndex = SelectedDocument is not null ? Documents.IndexOf(SelectedDocument) : 0,
        };

        foreach (var doc in Documents)
        {
            // Only persist file-backed tabs in session
            if (doc.FilePath is null) continue;

            state.OpenDocuments.Add(new SessionDocument
            {
                DocumentId = doc.DocumentId,
                FilePath = doc.FilePath,
                DisplayName = doc.DisplayName,
            });
        }

        _sessionStore.Save(state);
        _log?.Info("Session saved");
    }

    // --- Clean shutdown ---

    public void CleanupRecoveryOnShutdown()
    {
        // On clean shutdown, remove recovery snapshots for all documents
        if (_recoveryStore is null) return;

        foreach (var doc in Documents)
        {
            try
            {
                _recoveryStore.RemoveSnapshot(doc.DocumentId);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    // --- Document commands ---

    [RelayCommand]
    private void NewDocument()
    {
        var doc = DocumentViewModel.CreateNew();
        Documents.Add(doc);
        SelectedDocument = doc;
        StatusText = "New document created";
        NewDocumentCreated?.Invoke();
    }

    [RelayCommand]
    private async Task OpenDocumentAsync()
    {
        var result = await _fileService.OpenFileAsync();
        if (result is null)
            return;

        OpenDocumentFromPath(result.Value.Path, result.Value.Content);
    }

    [RelayCommand]
    private async Task OpenRecentFileAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var existing = Documents.FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedDocument = existing;
            StatusText = $"Switched to {existing.DisplayName}";
            return;
        }

        var content = await _fileService.ReadFileAsync(path);
        if (content is null)
        {
            RecentFiles.Remove(path);
            RecentFilesChanged?.Invoke();
            StatusText = $"File not found: {Path.GetFileName(path)}";
            return;
        }

        OpenDocumentFromPath(path, content);
    }

    /// <summary>
    /// Opens a file by path (e.g. from drag-and-drop). Does not modify
    /// the recent files list on failure — unlike OpenRecentFileAsync.
    /// </summary>
    public async Task OpenFileByPathAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        var existing = Documents.FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedDocument = existing;
            StatusText = $"Switched to {existing.DisplayName}";
            return;
        }

        var content = await _fileService.ReadFileAsync(path);
        if (content is null)
        {
            StatusText = $"Could not open: {Path.GetFileName(path)}";
            return;
        }

        OpenDocumentFromPath(path, content);
    }

    private void OpenDocumentFromPath(string path, string content)
    {
        var existing = Documents.FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedDocument = existing;
            StatusText = $"Switched to {existing.DisplayName}";
            return;
        }

        var doc = new DocumentViewModel();
        doc.BeginLoad();
        doc.FilePath = path;
        doc.DisplayName = Path.GetFileName(path);
        doc.SourceText = content;
        doc.EndLoad();
        doc.MarkClean();

        try { doc.LastKnownWriteTime = File.GetLastWriteTimeUtc(path); }
        catch { /* non-critical */ }

        Documents.Add(doc);
        SelectedDocument = doc;
        AddRecentFile(path);
        StatusText = $"Opened {doc.DisplayName}";
    }

    // --- Import ---

    /// <summary>
    /// Import a Word document: convert it to AsciiDoc at a location the user
    /// picks (images land in a media folder beside it) and open the result.
    /// The destination is asked for up front rather than importing into an
    /// untitled buffer, because the images need somewhere to live.
    /// </summary>
    [RelayCommand]
    private async Task ImportDocxAsync()
    {
        if (_docxImportService is null)
            return;

        var docxPath = await _fileService.PickOpenFilePathAsync(
            "Import Word Document", "Word Documents", ["*.docx"]);
        if (docxPath is null)
            return;

        var suggested = Path.GetFileNameWithoutExtension(docxPath) + ".adoc";
        var adocPath = await _fileService.PickSavePathAsync(suggested, "adoc", "AsciiDoc File");
        if (adocPath is null)
            return;

        StatusText = $"Importing {Path.GetFileName(docxPath)}...";

        var outcome = await _docxImportService.ImportAsync(docxPath, adocPath, CancellationToken.None);

        if (!outcome.IsSuccess)
        {
            _log?.Error($"Word import failed: {outcome.ErrorMessage}");
            StatusText = DocxImportSummary.Describe(outcome);
            return;
        }

        // Word carries plenty AsciiDoc can't model; log what was approximated
        // or lost so the summary line can stay short.
        foreach (var issue in outcome.Issues)
            _log?.Warn($"docx import: {issue}");

        await OpenFileByPathAsync(adocPath);
        StatusText = DocxImportSummary.Describe(outcome);
    }

    // --- Project workspace commands ---

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = await _fileService.PickFolderAsync();
        if (folder is null)
            return;
        LoadProjectFolder(folder);
    }

    /// <summary>Open <paramref name="folder"/> as the project workspace —
    /// also the session-restore entry point, so it must not throw.</summary>
    public void LoadProjectFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            StatusText = $"Folder not found: {folder}";
            ProjectFolderPath = null;
            return;
        }

        ProjectFolderPath = folder;
        RefreshProjectTree();
        ShowOutlinePane = true;
        SidebarFilesActive = true;
        StatusText = $"Folder opened: {Path.GetFileName(folder)}";
        _log?.Info($"Project folder opened: {folder}");
    }

    [RelayCommand]
    private void CloseFolder()
    {
        ProjectFolderPath = null;
        ProjectRoots.Clear();
        StatusText = "Folder closed";
    }

    [RelayCommand]
    private void RefreshProject() => RefreshProjectTree();

    // Bounds the tree so a folder accidentally opened at a drive root can't
    // hang the app; the status bar says when the cap was hit.
    private const int MaxProjectFiles = 5000;

    private void RefreshProjectTree()
    {
        ProjectRoots.Clear();
        if (ProjectFolderPath is null) return;

        try
        {
            var docs = EnumerateProjectDocuments();
            var capped = docs.Count > MaxProjectFiles;
            if (capped) docs.RemoveAt(docs.Count - 1);

            foreach (var node in ProjectTreeBuilder.Build(ProjectFolderPath, docs))
                ProjectRoots.Add(node);

            if (capped)
                StatusText = $"Folder is large — showing the first {MaxProjectFiles:N0} documents";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read folder: {ex.Message}";
            _log?.Error("Project tree refresh failed", ex);
        }
    }

    /// <summary>The project's documents as relative paths (capped at
    /// <see cref="MaxProjectFiles"/> + 1 so callers can detect the cap).</summary>
    private List<string> EnumerateProjectDocuments()
    {
        if (ProjectFolderPath is null) return [];

        var relative = Directory
            .EnumerateFiles(ProjectFolderPath, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(ProjectFolderPath, p));

        return ProjectTreeBuilder.FilterDocumentPaths(relative)
            .Take(MaxProjectFiles + 1)
            .ToList();
    }

    /// <summary>Export the whole project as a static HTML site.</summary>
    [RelayCommand]
    private async Task ExportWebsiteAsync()
    {
        if (ProjectFolderPath is null || _websiteExportService is null)
            return;

        List<string> docs;
        try
        {
            docs = EnumerateProjectDocuments();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read folder: {ex.Message}";
            return;
        }

        if (docs.Count == 0)
        {
            StatusText = "The project folder has no AsciiDoc documents";
            return;
        }

        var output = await _fileService.PickFolderAsync();
        if (output is null)
            return;

        StatusText = "Exporting website...";

        var result = await _websiteExportService.ExportAsync(
            ProjectFolderPath, docs, output, CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusText = $"Website exported — {docs.Count} page(s) → {output}";
        }
        else
        {
            _log?.Error($"Website export failed: {result.ErrorMessage}");
            await ReportExportFailureAsync(result.ErrorMessage ?? "Website export failed");
        }
    }

    [RelayCommand]
    private async Task SaveDocumentAsync()
    {
        if (SelectedDocument is null)
            return;

        if (SelectedDocument.FilePath is null)
        {
            await SaveDocumentAsAsync();
            return;
        }

        // Check for external file modification
        if (await CheckFileConflictAsync(SelectedDocument))
            return; // Conflict handled or save cancelled

        await _fileService.SaveFileAsync(SelectedDocument.FilePath, SelectedDocument.SourceText);
        SelectedDocument.MarkClean();
        SelectedDocument.IsRecovered = false;
        UpdateLastKnownWriteTime(SelectedDocument);
        RemoveRecoverySnapshot(SelectedDocument);
        StatusText = $"Saved {SelectedDocument.DisplayName}";
    }

    [RelayCommand]
    private async Task SaveDocumentAsAsync()
    {
        if (SelectedDocument is null)
            return;

        var path = await _fileService.SaveFileAsAsync(
            SelectedDocument.DisplayName, SelectedDocument.SourceText);
        if (path is null)
            return;

        SelectedDocument.FilePath = path;
        SelectedDocument.DisplayName = Path.GetFileName(path);
        SelectedDocument.MarkClean();
        SelectedDocument.IsRecovered = false;
        UpdateLastKnownWriteTime(SelectedDocument);
        RemoveRecoverySnapshot(SelectedDocument);
        AddRecentFile(path);
        StatusText = $"Saved {SelectedDocument.DisplayName}";
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        foreach (var doc in Documents.Where(d => d.IsDirty).ToList())
        {
            if (doc.FilePath is not null)
            {
                if (await CheckFileConflictAsync(doc))
                    continue; // Conflict handled (reload/saveas/cancel) — skip to next

                await _fileService.SaveFileAsync(doc.FilePath, doc.SourceText);
                doc.MarkClean();
                doc.IsRecovered = false;
                UpdateLastKnownWriteTime(doc);
                RemoveRecoverySnapshot(doc);
            }
            else
            {
                var prev = SelectedDocument;
                SelectedDocument = doc;

                var path = await _fileService.SaveFileAsAsync(doc.DisplayName, doc.SourceText);
                if (path is null)
                {
                    SelectedDocument = prev;
                    return; // User cancelled — stop Save All
                }

                doc.FilePath = path;
                doc.DisplayName = Path.GetFileName(path);
                doc.MarkClean();
                doc.IsRecovered = false;
                UpdateLastKnownWriteTime(doc);
                RemoveRecoverySnapshot(doc);
                AddRecentFile(path);
            }
        }

        StatusText = "All documents saved";
    }

    // --- File conflict detection ---

    /// <returns>true if save should be aborted (conflict handled externally).</returns>
    private async Task<bool> CheckFileConflictAsync(DocumentViewModel doc)
    {
        if (doc.FilePath is null || doc.LastKnownWriteTime is null)
            return false;

        try
        {
            var currentWriteTime = File.GetLastWriteTimeUtc(doc.FilePath);
            if (currentWriteTime <= doc.LastKnownWriteTime.Value)
                return false;
        }
        catch
        {
            return false; // File gone or inaccessible — let save proceed
        }

        _log?.Warn($"External modification detected: {doc.DisplayName}");

        var result = await _dialogService.ShowFileConflictAsync(doc.DisplayName);
        switch (result)
        {
            case FileConflictResult.Overwrite:
                return false; // Let save proceed

            case FileConflictResult.Reload:
                var content = await _fileService.ReadFileAsync(doc.FilePath);
                if (content is not null)
                {
                    doc.BeginLoad();
                    doc.SourceText = content;
                    doc.EndLoad();
                    doc.MarkClean();
                    UpdateLastKnownWriteTime(doc);
                    RemoveRecoverySnapshot(doc);
                    StatusText = $"Reloaded {doc.DisplayName} from disk";
                }
                return true;

            case FileConflictResult.SaveAs:
                await SaveDocumentAsAsync();
                return true;

            case FileConflictResult.Cancel:
            default:
                return true;
        }
    }

    private static void UpdateLastKnownWriteTime(DocumentViewModel doc)
    {
        if (doc.FilePath is null) return;
        try { doc.LastKnownWriteTime = File.GetLastWriteTimeUtc(doc.FilePath); }
        catch { /* non-critical */ }
    }

    private void RemoveRecoverySnapshot(DocumentViewModel doc)
    {
        try { _recoveryStore?.RemoveSnapshot(doc.DocumentId); }
        catch { /* non-critical */ }
    }

    // --- Export ---

    [RelayCommand]
    private Task ExportAsync(ExportFormat format) => ExportWithAsync(format, StylePath);

    /// <summary>Export once with another style; the default style is unchanged.</summary>
    [RelayCommand]
    private async Task ExportWithStyleAsync(ExportFormat format)
    {
        if (SelectedDocument is null || _exportService is null)
            return;

        var style = await PickStyleAsync();
        if (style is not null)
            await ExportWithAsync(format, style);
    }

    private Task<string?> PickStyleAsync()
        => _fileService.PickOpenFilePathAsync("Choose Style", "asciidoctor-pdf Theme", ["*.yml", "*.yaml"]);

    private async Task ExportWithAsync(ExportFormat format, string? stylePath)
    {
        if (SelectedDocument is null || _exportService is null)
            return;

        // The PDF's Title metadata: the document title per the user's rule,
        // optionally edited (cancelling the box cancels the export).
        string? pdfTitle = null;
        if (format is ExportFormat.Pdf or ExportFormat.PdfLandscape)
        {
            pdfTitle = PdfTitles.From(SelectedDocument.RenderedDocument?.Title, PdfTitleRule);
            if (AskPdfTitle)
            {
                var answer = await _dialogService.AskPdfTitleAsync(pdfTitle ?? "");
                if (answer is null)
                    return;
                // An emptied box means "no override": keep the document title.
                pdfTitle = string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
            }
        }

        var baseName = SelectedDocument.FilePath is not null
            ? Path.GetFileNameWithoutExtension(SelectedDocument.FilePath)
            : SelectedDocument.DisplayName.TrimEnd('*');

        var ext = format.Extension();
        var outputPath = await _fileService.PickSavePathAsync(
            $"{baseName}.{ext}", ext, format.FilterName());
        if (outputPath is null)
            return;

        StatusText = $"Exporting {format.DisplayName()}...";

        var basePath = SelectedDocument.FilePath is not null
            ? Path.GetDirectoryName(SelectedDocument.FilePath)
            : null;

        // Renders straight from the AsciiDoc source via AdocNet's converters —
        // no HTML intermediate and no double-parse against the preview pipeline.
        var result = await _exportService.ExportAsync(
            SelectedDocument.SourceText, basePath, outputPath, format, ExportTheme.Asciidoctor, stylePath, pdfTitle,
            CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusText = $"Exported to {Path.GetFileName(outputPath)}";
        }
        else
        {
            _log?.Error($"{format.DisplayName()} export failed: {result.ErrorMessage}");
            await ReportExportFailureAsync(result.ErrorMessage ?? $"{format.DisplayName()} export failed.");
        }
    }

    /// <summary>
    /// Export failures get a dialog: the status line alone is overwritten by
    /// the next render, so the failure would go unnoticed.
    /// </summary>
    private async Task ReportExportFailureAsync(string message)
    {
        StatusText = message;
        await _dialogService.ShowErrorAsync("Export Failed", message);
    }

    // --- Export profiles (single-source publishing) ---

    public ObservableCollection<ExportProfile> ExportProfiles { get; } = [];

    /// <summary>Raised when the profile list changes — the host persists it
    /// and rebuilds the Export menu.</summary>
    public event Action? ExportProfilesChanged;

    public void LoadExportProfiles(IEnumerable<ExportProfile> profiles)
    {
        ExportProfiles.Clear();
        foreach (var p in profiles)
            ExportProfiles.Add(p);
    }

    public void NotifyExportProfilesChanged() => ExportProfilesChanged?.Invoke();

    /// <summary>One-click export through a named profile: resolved output path
    /// when possible, save-dialog fallback for unsaved documents.</summary>
    [RelayCommand]
    private async Task ExportWithProfileAsync(ExportProfile? profile)
    {
        if (profile is null || SelectedDocument is null || _exportService is null)
            return;

        var outputPath = profile.ResolveOutputPath(
            SelectedDocument.FilePath, SelectedDocument.DisplayName.TrimEnd('*'));
        if (outputPath is null)
        {
            var ext = profile.Format.Extension();
            outputPath = await _fileService.PickSavePathAsync(
                $"document.{ext}", ext, profile.Format.FilterName());
            if (outputPath is null)
                return;
        }

        StatusText = $"Exporting {profile.Name}...";

        var basePath = SelectedDocument.FilePath is not null
            ? Path.GetDirectoryName(SelectedDocument.FilePath)
            : null;

        var result = await _exportService.ExportAsync(
            SelectedDocument.SourceText, basePath, outputPath, profile.Format, profile.Theme, StylePath,
            profile.Format is ExportFormat.Pdf or ExportFormat.PdfLandscape
                ? PdfTitles.From(SelectedDocument.RenderedDocument?.Title, PdfTitleRule)
                : null,
            CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusText = $"{profile.Name} → {Path.GetFileName(outputPath)}";
        }
        else
        {
            _log?.Error($"Profile '{profile.Name}' export failed: {result.ErrorMessage}");
            await ReportExportFailureAsync($"{profile.Name}: {result.ErrorMessage}");
        }
    }

    [RelayCommand]
    private void SetPdfTitleRule(PdfTitleRule rule) => PdfTitleRule = rule;

    [RelayCommand]
    private void ToggleAskPdfTitle() => AskPdfTitle = !AskPdfTitle;

    [RelayCommand]
    private async Task LoadStyleAsync()
    {
        var path = await PickStyleAsync();
        if (path is null)
            return;

        StylePath = path;
        StatusText = $"Style: {Path.GetFileName(path)}";
    }

    [RelayCommand]
    private void ResetStyle()
    {
        StylePath = null;
        StatusText = "Style: built-in";
    }

    // --- Close commands ---

    [RelayCommand]
    private async Task CloseDocumentAsync(DocumentViewModel? doc)
    {
        if (doc is null)
            return;

        if (doc.IsDirty)
        {
            var result = await _dialogService.ShowSaveConfirmationAsync(doc.DisplayName);
            switch (result)
            {
                case SaveConfirmationResult.Save:
                    SelectedDocument = doc;
                    await SaveDocumentAsync();
                    if (doc.IsDirty)
                        return; // Save was cancelled (e.g. user dismissed SaveAs)
                    break;
                case SaveConfirmationResult.Cancel:
                    return;
            }
        }

        // Remember closed file-backed docs for reopen
        if (doc.FilePath is not null)
            _closedTabPaths.Push(doc.FilePath);

        RemoveRecoverySnapshot(doc);

        var index = Documents.IndexOf(doc);
        Documents.Remove(doc);

        if (Documents.Count == 0)
        {
            SelectedDocument = null;
        }
        else if (SelectedDocument == doc)
        {
            SelectedDocument = Documents[Math.Min(index, Documents.Count - 1)];
        }

        StatusText = "Ready";
    }

    [RelayCommand]
    private async Task CloseTabAsync()
    {
        if (SelectedDocument is not null)
            await CloseDocumentAsync(SelectedDocument);
    }

    public async Task<bool> HandleAppClosingAsync()
    {
        foreach (var doc in Documents.Where(d => d.IsDirty).ToList())
        {
            var result = await _dialogService.ShowSaveConfirmationAsync(doc.DisplayName);
            switch (result)
            {
                case SaveConfirmationResult.Save:
                    if (doc.FilePath is not null)
                    {
                        if (await CheckFileConflictAsync(doc))
                        {
                            // Conflict handled — if user chose Cancel in conflict
                            // dialog, the doc is still dirty; abort close so they
                            // don't lose work.
                            if (doc.IsDirty)
                                return false;
                        }
                        else
                        {
                            await _fileService.SaveFileAsync(doc.FilePath, doc.SourceText);
                            doc.MarkClean();
                        }
                    }
                    else
                    {
                        var path = await _fileService.SaveFileAsAsync(doc.DisplayName, doc.SourceText);
                        if (path is null)
                            return false; // User cancelled SaveAs — abort close
                        doc.FilePath = path;
                        doc.MarkClean();
                    }
                    break;
                case SaveConfirmationResult.Cancel:
                    return false;
            }
        }

        return true;
    }

    // --- Zoom commands ---

    [RelayCommand]
    private void ZoomIn()
    {
        PreviewFitWidth = false;
        if (SelectedDocument is not null && SelectedDocument.ZoomLevel < DocumentViewModel.ZoomMax)
            SelectedDocument.ZoomLevel += DocumentViewModel.ZoomStep;
    }

    [RelayCommand]
    private void ZoomOut()
    {
        PreviewFitWidth = false;
        if (SelectedDocument is not null && SelectedDocument.ZoomLevel > DocumentViewModel.ZoomMin)
            SelectedDocument.ZoomLevel -= DocumentViewModel.ZoomStep;
    }

    [RelayCommand]
    private void ZoomReset()
    {
        PreviewFitWidth = false;
        if (SelectedDocument is not null)
            SelectedDocument.ZoomLevel = 100;
    }

    // --- Theme ---

    [RelayCommand]
    private void SetTheme(AppTheme theme) => CurrentTheme = theme;

    // --- Editor mode ---

    [RelayCommand]
    private void SetEditorMode(EditorMode mode) => EditorMode = mode;

    // --- Show Title & TOC toggle ---

    [RelayCommand]
    private void ToggleShowTitleAndToc() => ShowTitleAndToc = !ShowTitleAndToc;

    [RelayCommand]
    private void ToggleSyncScrolling() => SyncScrolling = !SyncScrolling;

    // --- Word wrap ---

    [RelayCommand]
    private void ToggleEditorWordWrap() => EditorWordWrap = !EditorWordWrap;

    [RelayCommand]
    private void TogglePreviewWordWrap() => PreviewWordWrap = !PreviewWordWrap;

    [RelayCommand]
    private void TogglePreviewFitWidth() => PreviewFitWidth = !PreviewFitWidth;

    // --- Outline pane ---

    [RelayCommand]
    private void ToggleOutlinePane() => ShowOutlinePane = !ShowOutlinePane;

    // --- Menu presentation: hamburger (☰) vs. inline File/View bar ---

    [RelayCommand]
    private void ToggleHamburgerMenu() => HamburgerMenu = !HamburgerMenu;

    [RelayCommand]
    private void NavigateToOutlineItem(OutlineItem? item)
    {
        if (item is not null)
            NavigateToLineRequested?.Invoke(item.LineNumber);
    }

    // --- Command palette ---

    [RelayCommand]
    private void ToggleCommandPalette()
    {
        if (CommandPalette.IsOpen)
            CommandPalette.Close();
        else
            CommandPalette.Open();
    }

    // --- Reopen closed tab ---

    [RelayCommand]
    private async Task ReopenClosedTabAsync()
    {
        while (_closedTabPaths.Count > 0)
        {
            var path = _closedTabPaths.Pop();
            var content = await _fileService.ReadFileAsync(path);
            if (content is not null)
            {
                OpenDocumentFromPath(path, content);
                return;
            }
        }
    }

    // --- Tab switching ---

    [RelayCommand]
    private void NextTab()
    {
        if (Documents.Count < 2) return;
        var idx = SelectedDocument is not null ? Documents.IndexOf(SelectedDocument) : -1;
        SelectedDocument = Documents[(idx + 1) % Documents.Count];
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (Documents.Count < 2) return;
        var idx = SelectedDocument is not null ? Documents.IndexOf(SelectedDocument) : 0;
        SelectedDocument = Documents[(idx - 1 + Documents.Count) % Documents.Count];
    }

    // --- Render pipeline ---

    partial void OnSelectedDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnSelectedDocumentPropertyChanged;

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedDocumentPropertyChanged;
            if (EditorMode != EditorMode.Source)
                RequestRender();
        }
    }

    partial void OnEditorModeChanged(EditorMode value)
    {
        if (value != EditorMode.Source && SelectedDocument is not null)
            RequestRender();

        OnPropertyChanged(nameof(IsSourceMode));
        OnPropertyChanged(nameof(IsSplitMode));
        OnPropertyChanged(nameof(IsWysiwygMode));
    }

    partial void OnCurrentThemeChanged(AppTheme value)
    {
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeSystem));
    }

    partial void OnShowTitleAndTocChanged(bool value)
    {
        if (EditorMode != EditorMode.Source && SelectedDocument is not null)
            RequestRender();
    }

    private void OnSelectedDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.SourceText) && EditorMode != EditorMode.Source)
            RequestRender();
    }

    public void RequestRender()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        PendingRenderTask = RenderAfterDelayAsync(_renderCts.Token);
    }

    private async Task RenderAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_debounceMs, ct);

            var doc = SelectedDocument;
            if (doc is null || ct.IsCancellationRequested)
                return;

            var source = doc.SourceText;

            doc.IsRendering = true;

            var basePath = doc.FilePath is not null
                ? Path.GetDirectoryName(doc.FilePath)
                : null;

            var result = await _renderService.RenderAsync(source, basePath, ShowTitleAndToc, ct);

            if (ct.IsCancellationRequested)
                return;

            doc.RenderedHtml = result.HtmlContent;
            // Set origins before the document so PreviewView (which reacts to
            // RenderedDocument) sees the matching line map for this render.
            doc.LineOrigins = result.LineOrigins;
            // Parser diagnostics: AdocNet 1.0.17 (Adoc.Net#67) reports these in
            // original source coordinates, so they map straight to editor lines.
            doc.Diagnostics = DiagnosticMapper.Map(result.Diagnostics);
            doc.RenderedDocument = result.Document;

            // Outline from the same parsed AST (title + section tree), mapped
            // back to editor lines via the render's line origins.
            OutlineItems.Clear();
            foreach (var item in OutlineBuilder.Build(result.Document, result.LineOrigins))
                OutlineItems.Add(item);
            doc.LastRenderSucceeded = result.IsSuccess;
            doc.RenderError = result.ErrorSummary;
            doc.IsRendering = false;

            StatusText = result.IsSuccess
                ? "Render complete"
                : $"Render failed: {result.ErrorSummary}";
        }
        catch (OperationCanceledException)
        {
            // Debounce or cancellation — expected, not an error
        }
    }

    // --- Recent files ---

    public void AddRecentFile(string path)
    {
        // Remove existing entry (case-insensitive)
        for (var i = RecentFiles.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentFiles[i], path, StringComparison.OrdinalIgnoreCase))
                RecentFiles.RemoveAt(i);
        }

        RecentFiles.Insert(0, path);

        while (RecentFiles.Count > MaxRecentFiles)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);

        RecentFilesChanged?.Invoke();
    }

    private List<CommandEntry> BuildCommandRegistry()
    {
        return
        [
            new("New Document", "Ctrl+N", "File", () => NewDocumentCommand.Execute(null)),
            new("Open Document", "Ctrl+O", "File", () => OpenDocumentCommand.Execute(null)),
            new("Open Folder", "Ctrl+Shift+O", "File", () => OpenFolderCommand.Execute(null)),
            new("Import from Word (.docx)", null, "File", () => ImportDocxCommand.Execute(null)),
            new("Close Folder", null, "File", () => CloseFolderCommand.Execute(null), () => HasProject),
            new("Refresh Folder", null, "File", () => RefreshProjectCommand.Execute(null), () => HasProject),
            new("Save", "Ctrl+S", "File", () => SaveDocumentCommand.Execute(null)),
            new("Save As", "Ctrl+Shift+S", "File", () => SaveDocumentAsCommand.Execute(null)),
            new("Save All", "Ctrl+Alt+S", "File", () => SaveAllCommand.Execute(null)),
            new("Close Tab", "Ctrl+W", "File", () => CloseTabCommand.Execute(null)),
            new("Reopen Closed Tab", "Ctrl+Shift+T", "File", () => ReopenClosedTabCommand.Execute(null)),
            new("Export as PDF", "Ctrl+E", "File", () => ExportCommand.Execute(ExportFormat.Pdf)),
            new("Export as PDF (Landscape)", null, "File", () => ExportCommand.Execute(ExportFormat.PdfLandscape)),
            new("Export as HTML", null, "File", () => ExportCommand.Execute(ExportFormat.Html)),
            new("Export as DocBook", null, "File", () => ExportCommand.Execute(ExportFormat.DocBook)),
            new("Export as EPUB", null, "File", () => ExportCommand.Execute(ExportFormat.Epub)),
            new("Export Website", null, "File", () => ExportWebsiteCommand.Execute(null), () => HasProject),
            new("Load Style", null, "Style", () => LoadStyleCommand.Execute(null)),
            new("Use Built-in Style", null, "Style", () => ResetStyleCommand.Execute(null)),
            new("Export as PDF with Style", null, "File", () => ExportWithStyleCommand.Execute(ExportFormat.Pdf)),
            new("PDF Title: Full Title", null, "File", () => SetPdfTitleRuleCommand.Execute(PdfTitleRule.FullTitle)),
            new("PDF Title: Up to First Colon", null, "File", () => SetPdfTitleRuleCommand.Execute(PdfTitleRule.UpToFirstColon)),
            new("Toggle Edit PDF Title Before Export", null, "File", () => ToggleAskPdfTitleCommand.Execute(null)),
            new("Export as HTML with Style", null, "File", () => ExportWithStyleCommand.Execute(ExportFormat.Html)),
            new("Export as EPUB with Style", null, "File", () => ExportWithStyleCommand.Execute(ExportFormat.Epub)),
            new("Source Mode", null, "View", () => SetEditorModeCommand.Execute(EditorMode.Source)),
            new("Split Mode", null, "View", () => SetEditorModeCommand.Execute(EditorMode.Split)),
            new("WYSIWYG Mode", null, "View", () => SetEditorModeCommand.Execute(EditorMode.Wysiwyg)),
            new("Toggle Title & TOC", null, "View", () => ToggleShowTitleAndTocCommand.Execute(null)),
            new("Toggle Sync Scrolling", null, "View", () => ToggleSyncScrollingCommand.Execute(null)),
            new("Toggle Editor Word Wrap", null, "View", () => ToggleEditorWordWrapCommand.Execute(null)),
            new("Toggle Preview Word Wrap", null, "View", () => TogglePreviewWordWrapCommand.Execute(null)),
            new("Toggle Page Preview", null, "View", () => TogglePagePreviewCommand.Execute(null)),
            new("Toggle Outline Pane", "Ctrl+B", "View", () => ToggleOutlinePaneCommand.Execute(null)),
            new("Zoom In", "Ctrl++", "View", () => ZoomInCommand.Execute(null)),
            new("Zoom Out", "Ctrl+-", "View", () => ZoomOutCommand.Execute(null)),
            new("Reset Zoom", "Ctrl+0", "View", () => ZoomResetCommand.Execute(null)),
            new("Toggle Fit Width", null, "View", () => TogglePreviewFitWidthCommand.Execute(null)),
            new("Theme: Light", null, "Theme", () => SetThemeCommand.Execute(AppTheme.Light)),
            new("Theme: Dark", null, "Theme", () => SetThemeCommand.Execute(AppTheme.Dark)),
            new("Theme: System", null, "Theme", () => SetThemeCommand.Execute(AppTheme.System)),
            new("Next Tab", "Ctrl+Tab", "Navigation", () => NextTabCommand.Execute(null)),
            new("Previous Tab", "Ctrl+Shift+Tab", "Navigation", () => PreviousTabCommand.Execute(null)),
            new("AsciiDoc Syntax Reference", "F1", "Help", () => ToggleSyntaxReferenceCommand.Execute(null)),
        ];
    }
}
