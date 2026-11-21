using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Models;
using Ascolotl.Core.Resources;
using Ascolotl.Core.Services;

namespace Ascolotl.Core.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly IRenderService _renderService;
    private readonly IExportService? _exportService;
    private readonly IWebsiteExportService? _websiteExportService;
    private readonly IDocumentImportService? _importService;
    private readonly IRecoveryStore? _recoveryStore;
    // Recovery entries this instance wrote or took over; shutdown removes only these,
    // so a second instance sharing the folder keeps its own.
    private readonly HashSet<string> _ownSnapshots = [];
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
    private string _statusText = Strings.Ready;

    [ObservableProperty]
    private EditorMode _editorMode = EditorMode.Wysiwyg;

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
    private PdfTitleRule _pdfTitleRule = PdfTitleRule.UpToFirstColon;

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
        string.Format(Strings.StyleCurrent, StylePath is null ? Strings.StyleBuiltIn : Path.GetFileName(StylePath).Replace("_", "__"));

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
    public bool IsLanguageSystem => UiLanguageCode == "";
    public bool IsLanguageEnglish => UiLanguageCode == "en";
    public bool IsLanguageFrench => UiLanguageCode == "fr";
    public bool IsLanguageGerman => UiLanguageCode == "de";
    public bool IsLanguageItalian => UiLanguageCode == "it";
    public bool IsLanguageSpanish => UiLanguageCode == "es";

    /// <summary>Interface language code, or "" for the system's. Takes effect at the next start.</summary>
    [ObservableProperty]
    private string _uiLanguageCode = "";

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];

    /// <summary>True when no document tabs are open — drives the empty-state view.</summary>
    public bool HasNoDocuments => Documents.Count == 0 && !OpeningDocuments;

    /// <summary>
    /// True while startup opens documents (restored tabs, files to open), so the
    /// empty-state screen doesn't flash before they arrive.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDocuments))]
    private bool _openingDocuments;

    public CommandPaletteViewModel CommandPalette { get; }

    /// <summary>Searchable AsciiDoc syntax reference (F1) — Coach-style help.</summary>
    public SyntaxReferenceViewModel SyntaxReference { get; } = new();

    [RelayCommand]
    private Task ShowAboutAsync() => _dialogService.ShowAboutAsync();

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
        IDocumentImportService? importService = null)
    {
        _fileService = fileService;
        _dialogService = dialogService;
        _renderService = renderService;
        _exportService = exportService;
        _websiteExportService = websiteExportService;
        _importService = importService;
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
        // The timer fires on the thread pool; documents belong to the UI thread.
        var ui = SynchronizationContext.Current;
        _autosaveTimer = new System.Threading.Timer(
            _ =>
            {
                if (ui is null) RunAutosave();
                else ui.Post(_ => { if (_autosaveTimer is not null) RunAutosave(); }, null);
            }, null,
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
                _ownSnapshots.Add(doc.DocumentId);
                _recoveryStore.SaveSnapshot(
                    doc.DocumentId, doc.SourceText, doc.FilePath, doc.DisplayName, doc.LastKnownWriteTime);
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
                    doc.DocumentId, doc.SourceText, doc.FilePath, doc.DisplayName, doc.LastKnownWriteTime);
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
        foreach (var entry in entries)
            _ownSnapshots.Add(entry.DocumentId);

        var shouldRecover = await _dialogService.ShowRecoveryDialogAsync(entries.Count);
        if (!shouldRecover)
        {
            _recoveryStore.ClearAll();
            _log?.Info("User discarded recovery data");
            return false;
        }

        var recovered = 0;
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

            // The write time the edits were based on, so the first save flags
            // changes made to the file while the app was down. Older manifests
            // lack it: fall back to the file's current time.
            doc.LastKnownWriteTime = entry.FileWriteTime;
            if (doc.LastKnownWriteTime is null && entry.OriginalFilePath is not null)
            {
                try { doc.LastKnownWriteTime = File.GetLastWriteTimeUtc(entry.OriginalFilePath); }
                catch { /* file may be gone — conflict check will be skipped */ }
            }
            // Save back in the file's own encoding (BOM, UTF-16, 1252), not plain UTF-8.
            if (entry.OriginalFilePath is not null)
            {
                try { doc.FileEncoding = TextFile.Read(entry.OriginalFilePath).Encoding; }
                catch { /* gone or unreadable: UTF-8 */ }
            }

            Documents.Add(doc);
            recovered++;
        }

        if (recovered == 0)
            return false;

        SelectedDocument = Documents[0];

        // Do NOT ClearAll here — recovery data stays on disk as a safety net
        // until each document is explicitly saved (RemoveRecoverySnapshot) or
        // the app shuts down cleanly (CleanupRecoveryOnShutdown).
        _log?.Info(string.Format(Strings.StatusRecovered, recovered));
        StatusText = string.Format(Strings.StatusRecovered, recovered);
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
            var results = new List<(SessionDocument Session, FileText Content, DateTime WriteTime)>();
            foreach (var sessionDoc in session.OpenDocuments)
            {
                if (sessionDoc.FilePath is null) continue;
                try
                {
                    var content = TextFile.Read(sessionDoc.FilePath);
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

        var recovered = Documents.Count > 0; // recovered documents (opened first) keep the focus
        foreach (var (sessionDoc, content, writeTime) in loaded)
        {
            // Skip if already open (e.g. from recovery)
            if (Documents.Any(d => string.Equals(d.FilePath, sessionDoc.FilePath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var doc = new DocumentViewModel { DocumentId = sessionDoc.DocumentId };
            doc.BeginLoad();
            doc.FilePath = sessionDoc.FilePath;
            doc.DisplayName = sessionDoc.DisplayName;
            doc.SourceText = content.Text;
            doc.EndLoad();
            doc.MarkClean();
            doc.LastKnownWriteTime = writeTime;
            doc.FileEncoding = content.Encoding;
            doc.TopLine = sessionDoc.TopLine;
            if (Enum.IsDefined(sessionDoc.EditorMode)) doc.EditorMode = sessionDoc.EditorMode;

            Documents.Add(doc);
        }

        // By path: a file that failed to load shifts the indexes. Set even when
        // a tab is already selected, as the tab strip selects the first one added.
        if (!recovered && Documents.Count > 0)
        {
            var front = session.SelectedIndex >= 0 && session.SelectedIndex < session.OpenDocuments.Count
                ? session.OpenDocuments[session.SelectedIndex].FilePath
                : null;
            SelectedDocument = Documents.FirstOrDefault(
                d => string.Equals(d.FilePath, front, StringComparison.OrdinalIgnoreCase)) ?? Documents[0];
        }

        if (Documents.Count > 0)
        {
            _log?.Info($"Restored session with {Documents.Count} document(s)");
            StatusText = string.Format(Strings.StatusSessionRestored, Documents.Count);
        }
    }

    public void SaveSession()
    {
        if (_sessionStore is null) return;

        var state = new SessionState();

        foreach (var doc in Documents)
        {
            // Only persist file-backed tabs in session
            if (doc.FilePath is null) continue;

            if (doc == SelectedDocument)
                state.SelectedIndex = state.OpenDocuments.Count;
            state.OpenDocuments.Add(new SessionDocument
            {
                DocumentId = doc.DocumentId,
                FilePath = doc.FilePath,
                DisplayName = doc.DisplayName,
                TopLine = doc.TopLine,
                EditorMode = doc.EditorMode,
            });
        }

        _sessionStore.Save(state);
        _log?.Info("Session saved");
    }

    // --- Clean shutdown ---

    public void CleanupRecoveryOnShutdown()
    {
        // Every dirty document was saved or deliberately discarded in
        // HandleAppClosingAsync, so none of this instance's entries is still needed.
        foreach (var doc in Documents)
            DeleteAssets(doc.AssetDirectory);
        if (_recoveryStore is null) return;
        foreach (var id in _ownSnapshots)
        {
            // One locked file must not keep the rest for a false "recover" offer next launch.
            try { _recoveryStore.RemoveSnapshot(id); }
            catch (Exception ex) { _log?.Error($"Could not remove recovery entry {id}", ex); }
        }
    }

    // --- Document commands ---

    [RelayCommand]
    private void NewDocument()
    {
        var doc = DocumentViewModel.CreateNew();
        Documents.Add(doc);
        SelectedDocument = doc;
        StatusText = Strings.StatusNewDocument;
        NewDocumentCreated?.Invoke();
    }

    [RelayCommand]
    private async Task OpenDocumentAsync()
    {
        var result = await _fileService.OpenFileAsync();
        if (result is null)
            return;

        if (result.Value.Content is not { } content)
        {
            StatusText = string.Format(Strings.StatusCouldNotOpen, Path.GetFileName(result.Value.Path));
            return;
        }

        OpenDocumentFromPath(result.Value.Path, content);
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
            StatusText = string.Format(Strings.StatusSwitchedTo, existing.DisplayName);
            return;
        }

        var content = await _fileService.ReadFileAsync(path);
        if (content is null)
        {
            // Only a vanished file leaves the list; a locked one stays.
            if (File.Exists(path))
            {
                StatusText = string.Format(Strings.StatusCouldNotOpen, Path.GetFileName(path));
                await _dialogService.ShowErrorAsync(Strings.CouldNotOpenTitle, string.Format(Strings.RecentFileUnreadable, path));
                return;
            }
            RecentFiles.Remove(path);
            RecentFilesChanged?.Invoke();
            StatusText = string.Format(Strings.StatusFileNotFound, Path.GetFileName(path));
            await _dialogService.ShowErrorAsync(Strings.FileNotFoundTitle, string.Format(Strings.RecentFileMissing, path));
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
            StatusText = string.Format(Strings.StatusSwitchedTo, existing.DisplayName);
            return;
        }

        var content = await _fileService.ReadFileAsync(path);
        if (content is null)
        {
            StatusText = string.Format(Strings.StatusCouldNotOpen, Path.GetFileName(path));
            return;
        }

        OpenDocumentFromPath(path, content);
    }

    private void OpenDocumentFromPath(string path, FileText content)
    {
        var existing = Documents.FirstOrDefault(
            d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedDocument = existing;
            StatusText = string.Format(Strings.StatusSwitchedTo, existing.DisplayName);
            return;
        }

        var doc = new DocumentViewModel();
        doc.BeginLoad();
        doc.FilePath = path;
        doc.DisplayName = Path.GetFileName(path);
        doc.SourceText = content.Text;
        doc.EndLoad();
        doc.MarkClean();
        doc.FileEncoding = content.Encoding;

        try { doc.LastKnownWriteTime = File.GetLastWriteTimeUtc(path); }
        catch { /* non-critical */ }

        Documents.Add(doc);
        SelectedDocument = doc;
        AddRecentFile(path);
        StatusText = string.Format(Strings.StatusOpened, doc.DisplayName);
    }

    // --- Import ---

    /// <summary>
    /// Import: convert a file in another format into a new, unsaved AsciiDoc
    /// tab named after it. Extracted images wait in a private folder that the
    /// preview resolves against; the first save copies them beside the file.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync(ImportFormat format)
    {
        if (format != ImportFormat.PlainText && _importService is null)
            return;

        var sourcePath = await _fileService.PickOpenFilePathAsync(
            string.Format(Strings.ImportDialogTitle, format.DisplayName()), format.DisplayName(), format.Patterns());
        if (sourcePath is null)
            return;

        var name = Path.GetFileNameWithoutExtension(sourcePath);
        StatusText = string.Format(Strings.StatusImporting, Path.GetFileName(sourcePath));

        if (format == ImportFormat.PlainText)
        {
            var text = await _fileService.ReadFileAsync(sourcePath);
            if (text is null)
            {
                await ReportFailureAsync(Strings.ImportFailedTitle, string.Format(Strings.ImportCouldNotRead, Path.GetFileName(sourcePath)));
                return;
            }
            OpenImported(name, PlainTextImport.ToAsciiDoc(text.Text, name), assetDirectory: null);
            StatusText = string.Format(Strings.StatusImported, Path.GetFileName(sourcePath));
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), "Ascolotl", "imports", Guid.NewGuid().ToString("N"));
        var adocPath = Path.Combine(workDir, name + ".adoc");
        string source;
        ImportOutcome outcome;
        try
        {
            Directory.CreateDirectory(workDir);
            outcome = await _importService!.ImportAsync(format, sourcePath, adocPath, CancellationToken.None);
            source = outcome.IsSuccess ? (await TextFile.ReadAsync(adocPath)).Text : "";
            if (outcome.IsSuccess)
                File.Delete(adocPath);
        }
        catch (Exception ex)
        {
            outcome = ImportOutcome.Failure(string.Format(Strings.ImportCouldNotReadConverted, ex.Message), ex);
            source = "";
        }
        if (!outcome.IsSuccess)
        {
            DeleteAssets(workDir);
            _log?.Error($"Import of {sourcePath} failed: {outcome.ErrorMessage}", outcome.Exception);
            await ReportFailureAsync(Strings.ImportFailedTitle, ImportSummary.Describe(outcome));
            return;
        }

        // Other formats carry plenty AsciiDoc can't model; log what was approximated
        // or lost so the summary line can stay short.
        foreach (var issue in outcome.Issues)
            _log?.Warn($"{format.DisplayName()} import: {issue}");

        var hasAssets = Directory.EnumerateFileSystemEntries(workDir).Any();
        if (!hasAssets)
            DeleteAssets(workDir);
        OpenImported(name, source, hasAssets ? workDir : null);
        StatusText = ImportSummary.Describe(outcome);
    }

    private void OpenImported(string name, string source, string? assetDirectory)
    {
        var doc = new DocumentViewModel { DisplayName = name + ".adoc", AssetDirectory = assetDirectory };
        doc.SourceText = source; // dirty: closing asks whether to save it
        Documents.Add(doc);
        SelectedDocument = doc;
    }

    /// <summary>
    /// First save of an import: copy its images beside the saved file, keeping
    /// their relative paths, then drop the private folder. An existing file of
    /// the same name is kept, never overwritten.
    /// </summary>
    /// <returns>The image files left as they were because one already existed.</returns>
    private List<string> MoveImportedAssets(DocumentViewModel doc, string targetDir)
    {
        var kept = new List<string>();
        if (doc.AssetDirectory is not { } assets)
            return kept;
        foreach (var file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(targetDir, Path.GetRelativePath(assets, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                kept.Add(Path.GetRelativePath(targetDir, target));
                _log?.Warn($"Import image not copied, {target} already exists");
                continue;
            }
            File.Copy(file, target);
        }
        doc.AssetDirectory = null;
        DeleteAssets(assets);
        if (SelectedDocument == doc)
            RequestRender(immediate: true); // the preview's images pointed into the deleted folder
        return kept;
    }

    private void DeleteAssets(string? directory)
    {
        if (directory is null)
            return;
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception ex) { _log?.Warn($"Could not delete {directory}: {ex.Message}"); }
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
            StatusText = string.Format(Strings.StatusFolderNotFound, folder);
            ProjectFolderPath = null;
            return;
        }

        ProjectFolderPath = folder;
        RefreshProjectTree();
        ShowOutlinePane = true;
        SidebarFilesActive = true;
        StatusText = string.Format(Strings.StatusFolderOpened, Path.GetFileName(folder));
        _log?.Info($"Project folder opened: {folder}");
    }

    [RelayCommand]
    private void CloseFolder()
    {
        ProjectFolderPath = null;
        ProjectRoots.Clear();
        StatusText = Strings.StatusFolderClosed;
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
                StatusText = string.Format(Strings.StatusFolderLarge, MaxProjectFiles);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(Strings.StatusCouldNotReadFolder, ex.Message);
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
            StatusText = string.Format(Strings.StatusCouldNotReadFolder, ex.Message);
            return;
        }

        if (docs.Count == 0)
        {
            StatusText = Strings.StatusFolderHasNoDocuments;
            return;
        }

        var output = await _fileService.PickFolderAsync();
        if (output is null)
            return;

        StatusText = Strings.StatusExportingWebsite;

        var result = await _websiteExportService.ExportAsync(
            ProjectFolderPath, docs, output, CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusText = string.Format(Strings.StatusWebsiteExported, docs.Count, output);
        }
        else
        {
            _log?.Error($"Website export failed: {result.ErrorMessage}");
            await ReportExportFailureAsync(result.ErrorMessage ?? Strings.WebsiteExportFailed);
        }
    }

    [RelayCommand]
    private async Task SaveDocumentAsync()
    {
        if (SelectedDocument is not { } doc)
            return;

        // Check for external file modification
        if (doc.FilePath is not null && await CheckFileConflictAsync(doc))
            return; // Conflict handled or save cancelled

        if (await WriteDocumentAsync(doc, saveAs: false))
            StatusText = string.Format(Strings.StatusSaved, doc.DisplayName);
    }

    [RelayCommand]
    private Task SaveDocumentAsAsync()
        => SelectedDocument is { } doc ? SaveAsAsync(doc) : Task.CompletedTask;

    private async Task SaveAsAsync(DocumentViewModel doc)
    {
        if (await WriteDocumentAsync(doc, saveAs: true))
            StatusText = string.Format(Strings.StatusSaved, doc.DisplayName);
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

                if (!await WriteDocumentAsync(doc, saveAs: false))
                    return; // Failure reported — stop Save All
            }
            else
            {
                var prev = SelectedDocument;
                SelectedDocument = doc;

                if (!await WriteDocumentAsync(doc, saveAs: true))
                {
                    SelectedDocument = prev;
                    return; // User cancelled or failure reported — stop Save All
                }
            }
        }

        StatusText = Strings.StatusAllSaved;
    }

    /// <summary>
    /// The one place documents reach the disk: writes <paramref name="doc"/>
    /// (to a picked path when <paramref name="saveAs"/> or untitled), keeping
    /// its encoding. A failure is reported and leaves the document dirty.
    /// </summary>
    /// <returns>true when written; false when cancelled or failed.</returns>
    /// <summary>
    /// Set by the preview: puts an open in-place edit's text into its document,
    /// so saving and closing see it.
    /// </summary>
    public Action? FlushPendingEdits { get; set; }

    /// <summary>Set by the preview: drops an open in-place edit's changes.</summary>
    public Action? CancelPendingEdits { get; set; }

    /// <summary>An in-place edit is open in the preview; the toolbar offers Apply and Cancel.</summary>
    [ObservableProperty]
    private bool _inPlaceEditOpen;

    private async Task<bool> WriteDocumentAsync(DocumentViewModel doc, bool saveAs)
    {
        FlushPendingEdits?.Invoke();
        // Typing continues during the await: only what was written counts as saved.
        var text = doc.SourceText;
        var encoding = TextFile.EncodingFor(text, doc.FileEncoding);
        string? path;
        try
        {
            if (saveAs || doc.FilePath is null)
            {
                path = await _fileService.SaveFileAsAsync(doc.DisplayName, text, encoding);
                if (path is null)
                    return false;
            }
            else
            {
                path = doc.FilePath;
                await _fileService.SaveFileAsync(path, text, encoding);
            }
        }
        catch (Exception ex)
        {
            _log?.Error($"Save failed: {doc.DisplayName}", ex);
            await ReportFailureAsync(Strings.SaveFailedTitle, string.Format(Strings.SaveCouldNotSave, doc.DisplayName, ex.Message));
            return false;
        }

        if (doc.AssetDirectory is not null)
        {
            try
            {
                var kept = MoveImportedAssets(doc, Path.GetDirectoryName(path)!);
                if (kept.Count > 0)
                    await ReportFailureAsync(Strings.ImagesNotReplacedTitle,
                        string.Format(Strings.ImagesNotReplaced, doc.DisplayName, string.Join("\n", kept)));
            }
            catch (Exception ex)
            {
                // The text is saved; the images stay in the private folder for the next save.
                _log?.Error($"Copying imported images beside {path} failed", ex);
                await ReportFailureAsync(Strings.ImagesNotCopiedTitle, string.Format(Strings.ImagesNotCopied, doc.DisplayName, ex.Message));
            }
        }
        if (!string.Equals(path, doc.FilePath, StringComparison.Ordinal))
        {
            doc.FilePath = path;
            doc.DisplayName = Path.GetFileName(path);
            AddRecentFile(path);
        }
        doc.FileEncoding = encoding;
        doc.IsRecovered = false;
        UpdateLastKnownWriteTime(doc);
        if (doc.SourceText == text)
        {
            doc.MarkClean();
            RemoveRecoverySnapshot(doc);
        }
        return true;
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
                    doc.SourceText = content.Text;
                    doc.EndLoad();
                    doc.MarkClean();
                    doc.FileEncoding = content.Encoding;
                    UpdateLastKnownWriteTime(doc);
                    RemoveRecoverySnapshot(doc);
                    StatusText = string.Format(Strings.StatusReloaded, doc.DisplayName);
                }
                return true;

            case FileConflictResult.SaveAs:
                await SaveAsAsync(doc);
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
        => _fileService.PickOpenFilePathAsync(Strings.DialogChooseStyle, Strings.FilterPdfTheme, ["*.yml", "*.yaml"]);

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

        StatusText = string.Format(Strings.StatusExporting, format.DisplayName());

        var basePath = SelectedDocument.BaseDirectory;

        // Renders straight from the AsciiDoc source via AdocNet's converters —
        // no HTML intermediate and no double-parse against the preview pipeline.
        var result = await _exportService.ExportAsync(
            SelectedDocument.SourceText, SelectedDocument.IsMarkdown, basePath, outputPath, format, ExportTheme.Asciidoctor, stylePath,
            new PdfExportOptions(pdfTitle, AccessiblePdf), CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusText = ExportedStatus(Path.GetFileName(outputPath), result);
        }
        else
        {
            _log?.Error($"{format.DisplayName()} export failed: {result.ErrorMessage}");
            await ReportExportFailureAsync(result.ErrorMessage ?? string.Format(Strings.ExportFailed, format.DisplayName()));
        }
    }

    /// <summary>
    /// Export failures get a dialog: the status line alone is overwritten by
    /// the next render, so the failure would go unnoticed.
    /// </summary>
    private Task ReportExportFailureAsync(string message) => ReportFailureAsync(Strings.ExportFailedTitle, message);

    private async Task ReportFailureAsync(string title, string message)
    {
        StatusText = message;
        await _dialogService.ShowErrorAsync(title, message);
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

        StatusText = string.Format(Strings.StatusExporting, profile.Name);

        var basePath = SelectedDocument.BaseDirectory;

        var result = await _exportService.ExportAsync(
            SelectedDocument.SourceText, SelectedDocument.IsMarkdown, basePath, outputPath, profile.Format, profile.Theme, StylePath,
            new PdfExportOptions(
                profile.Format is ExportFormat.Pdf or ExportFormat.PdfLandscape
                    ? PdfTitles.From(SelectedDocument.RenderedDocument?.Title, PdfTitleRule)
                    : null,
                AccessiblePdf),
            CancellationToken.None);

        if (result.IsSuccess)
        {
            StatusText = ExportedStatus($"{profile.Name} → {Path.GetFileName(outputPath)}", result);
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

    /// <summary>PDF exports are tagged PDF/UA-1: readable by screen readers, every font embedded.</summary>
    [ObservableProperty]
    private bool _accessiblePdf;

    [RelayCommand]
    private void ToggleAccessiblePdf() => AccessiblePdf = !AccessiblePdf;

    /// <summary>The export's status line, noting what an accessible PDF had to work around.</summary>
    private string ExportedStatus(string exported, ExportResult result)
    {
        foreach (var warning in result.Warnings)
            _log?.Warn($"Export: {warning}");
        return result.Warnings.Count == 0
            ? string.Format(Strings.StatusExportedTo, exported)
            : string.Format(Strings.StatusExportedWithWarnings, exported, result.Warnings.Count);
    }

    [RelayCommand]
    private async Task LoadStyleAsync()
    {
        var path = await PickStyleAsync();
        if (path is null)
            return;

        StylePath = path;
        StatusText = string.Format(Strings.StatusStyle, Path.GetFileName(path));
    }

    [RelayCommand]
    private void ResetStyle()
    {
        StylePath = null;
        StatusText = Strings.StatusStyleBuiltIn;
    }

    // --- Close commands ---

    [RelayCommand]
    private async Task CloseDocumentAsync(DocumentViewModel? doc)
    {
        if (doc is null)
            return;

        FlushPendingEdits?.Invoke();
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
        DeleteAssets(doc.AssetDirectory);

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

        StatusText = Strings.Ready;
    }

    [RelayCommand]
    private async Task CloseTabAsync()
    {
        if (SelectedDocument is not null)
            await CloseDocumentAsync(SelectedDocument);
    }

    public async Task<bool> HandleAppClosingAsync()
    {
        FlushPendingEdits?.Invoke();
        foreach (var doc in Documents.Where(d => d.IsDirty).ToList())
        {
            var result = await _dialogService.ShowSaveConfirmationAsync(doc.DisplayName);
            switch (result)
            {
                case SaveConfirmationResult.Save:
                    // A conflict answered with Reload/Save As is handled; otherwise write.
                    if (doc.FilePath is null || !await CheckFileConflictAsync(doc))
                        await WriteDocumentAsync(doc, saveAs: false);
                    // Still dirty (cancelled, failed, edited meanwhile): abort close so no work is lost.
                    if (doc.IsDirty)
                        return false;
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
        // Steps land on multiples of 10 even after a smooth gesture left 117 %.
        if (SelectedDocument is { } doc)
            ZoomTo((doc.ZoomLevel / DocumentViewModel.ZoomStep + 1) * DocumentViewModel.ZoomStep);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (SelectedDocument is { } doc)
            ZoomTo((doc.ZoomLevel - 1) / DocumentViewModel.ZoomStep * DocumentViewModel.ZoomStep);
    }

    // Exact zoom between whole percents, so a gesture's many small steps add up
    // instead of each rounding back to the same level.
    private double _zoomExact;

    /// <summary>Scales the zoom by <paramref name="factor"/> (smooth Ctrl+wheel or pinch zoom).</summary>
    public void ZoomBy(double factor)
    {
        if (SelectedDocument is not { } doc)
            return;
        if (Math.Abs(_zoomExact - doc.ZoomLevel) >= 1)
            _zoomExact = doc.ZoomLevel; // changed elsewhere (menu, another tab)
        _zoomExact = Math.Clamp(_zoomExact * factor, DocumentViewModel.ZoomMin, DocumentViewModel.ZoomMax);
        ZoomTo((int)Math.Round(_zoomExact));
    }

    /// <summary>Sets the zoom (clamped to its range), e.g. from a smooth gesture; turns Fit Width off.</summary>
    public void ZoomTo(int percent)
    {
        PreviewFitWidth = false;
        if (SelectedDocument is not null)
            SelectedDocument.ZoomLevel = Math.Clamp(percent, DocumentViewModel.ZoomMin, DocumentViewModel.ZoomMax);
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

    // --- Interface language ---

    [RelayCommand]
    private void SetUiLanguage(string code)
    {
        if (code == UiLanguageCode)
            return;
        UiLanguageCode = code;
        // Said in the chosen language: it is the one the user reads from now on.
        var culture = code == "" ? System.Globalization.CultureInfo.InstalledUICulture : System.Globalization.CultureInfo.GetCultureInfo(code);
        StatusText = Strings.ResourceManager.GetString(nameof(Strings.LanguageAppliesAfterRestart), culture) ?? Strings.LanguageAppliesAfterRestart;
    }

    partial void OnUiLanguageCodeChanged(string value)
    {
        OnPropertyChanged(nameof(IsLanguageSystem));
        OnPropertyChanged(nameof(IsLanguageEnglish));
        OnPropertyChanged(nameof(IsLanguageFrench));
        OnPropertyChanged(nameof(IsLanguageGerman));
        OnPropertyChanged(nameof(IsLanguageItalian));
        OnPropertyChanged(nameof(IsLanguageSpanish));
    }

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
            if (EditorMode != newValue.EditorMode)
                EditorMode = newValue.EditorMode; // renders the document unless Source
            else if (EditorMode != EditorMode.Source)
                RequestRender(immediate: true);
        }
    }

    partial void OnEditorModeChanged(EditorMode value)
    {
        if (SelectedDocument is not null)
            SelectedDocument.EditorMode = value;
        if (value != EditorMode.Source && SelectedDocument is not null)
            RequestRender(immediate: true);

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
        // Saving as .md (or back) changes the parser, not the text.
        if (e.PropertyName is nameof(DocumentViewModel.SourceText) or nameof(DocumentViewModel.IsMarkdown)
            && EditorMode != EditorMode.Source)
            RequestRender();
    }

    /// <summary>
    /// Renders the selected document. Typing waits out the debounce so each
    /// keystroke doesn't re-render; <paramref name="immediate"/> skips it for a
    /// document just shown (startup, tab switch, mode change).
    /// </summary>
    public void RequestRender(bool immediate = false)
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        PendingRenderTask = RenderAfterDelayAsync(immediate ? 0 : _debounceMs, _renderCts.Token);
    }

    private async Task RenderAfterDelayAsync(int delayMs, CancellationToken ct)
    {
        try
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, ct);
            else
                // One dispatcher turn: a selection that changes again in the same
                // step (session restore: the tab strip selects the first tab, then
                // the saved one) cancels this render before it does any work.
                await Task.Yield();

            var doc = SelectedDocument;
            if (doc is null || ct.IsCancellationRequested)
                return;

            var source = doc.SourceText;

            var basePath = doc.BaseDirectory;

            var result = await _renderService.RenderAsync(source, basePath, doc.IsMarkdown, ShowTitleAndToc, ct);

            if (ct.IsCancellationRequested)
                return;

            // Set origins before the document so PreviewView (which reacts to
            // RenderedDocument) sees the matching line map for this render.
            doc.LineOrigins = result.LineOrigins;
            doc.RenderedSource = source;
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

            StatusText = result.IsSuccess
                ? Strings.StatusRenderComplete
                : string.Format(Strings.StatusRenderFailed, result.ErrorSummary);
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
            new(Strings.CommandNewDocument, "Ctrl+N", Strings.CommandCategoryFile, () => NewDocumentCommand.Execute(null)),
            new(Strings.CommandOpenDocument, "Ctrl+O", Strings.CommandCategoryFile, () => OpenDocumentCommand.Execute(null)),
            new(Strings.CommandOpenFolder, "Ctrl+Shift+O", Strings.CommandCategoryFile, () => OpenFolderCommand.Execute(null)),
            new(Strings.CommandImportWordDocument, null, Strings.CommandCategoryImport, () => ImportCommand.Execute(ImportFormat.Word)),
            new(Strings.CommandImportMarkdown, null, Strings.CommandCategoryImport, () => ImportCommand.Execute(ImportFormat.Markdown)),
            new(Strings.CommandImportHTML, null, Strings.CommandCategoryImport, () => ImportCommand.Execute(ImportFormat.Html)),
            new(Strings.CommandImportPDF, null, Strings.CommandCategoryImport, () => ImportCommand.Execute(ImportFormat.Pdf)),
            new(Strings.CommandImportPlainText, null, Strings.CommandCategoryImport, () => ImportCommand.Execute(ImportFormat.PlainText)),
            new(Strings.CommandCloseFolder, null, Strings.CommandCategoryFile, () => CloseFolderCommand.Execute(null), () => HasProject),
            new(Strings.CommandRefreshFolder, null, Strings.CommandCategoryFile, () => RefreshProjectCommand.Execute(null), () => HasProject),
            new(Strings.CommandSave, "Ctrl+S", Strings.CommandCategoryFile, () => SaveDocumentCommand.Execute(null)),
            new(Strings.CommandSaveAs, "Ctrl+Shift+S", Strings.CommandCategoryFile, () => SaveDocumentAsCommand.Execute(null)),
            new(Strings.CommandSaveAll, "Ctrl+Alt+S", Strings.CommandCategoryFile, () => SaveAllCommand.Execute(null)),
            new(Strings.CommandCloseTab, "Ctrl+W", Strings.CommandCategoryFile, () => CloseTabCommand.Execute(null)),
            new(Strings.CommandReopenClosedTab, "Ctrl+Shift+T", Strings.CommandCategoryFile, () => ReopenClosedTabCommand.Execute(null)),
            new(Strings.CommandExportAsPDF, "Ctrl+E", Strings.CommandCategoryFile, () => ExportCommand.Execute(ExportFormat.Pdf)),
            new(Strings.CommandExportAsPDFLandscape, null, Strings.CommandCategoryFile, () => ExportCommand.Execute(ExportFormat.PdfLandscape)),
            new(Strings.CommandExportAsHTML, null, Strings.CommandCategoryFile, () => ExportCommand.Execute(ExportFormat.Html)),
            new(Strings.CommandExportAsDocBook, null, Strings.CommandCategoryFile, () => ExportCommand.Execute(ExportFormat.DocBook)),
            new(Strings.CommandExportAsEPUB, null, Strings.CommandCategoryFile, () => ExportCommand.Execute(ExportFormat.Epub)),
            new(Strings.CommandExportWebsite, null, Strings.CommandCategoryFile, () => ExportWebsiteCommand.Execute(null), () => HasProject),
            new(Strings.CommandLoadStyle, null, Strings.CommandCategoryStyle, () => LoadStyleCommand.Execute(null)),
            new(Strings.CommandUseBuiltInStyle, null, Strings.CommandCategoryStyle, () => ResetStyleCommand.Execute(null)),
            new(Strings.CommandExportAsPDFWithStyle, null, Strings.CommandCategoryFile, () => ExportWithStyleCommand.Execute(ExportFormat.Pdf)),
            new(Strings.CommandPDFTitleFullTitle, null, Strings.CommandCategoryFile, () => SetPdfTitleRuleCommand.Execute(PdfTitleRule.FullTitle)),
            new(Strings.CommandPDFTitleUpToFirstColon, null, Strings.CommandCategoryFile, () => SetPdfTitleRuleCommand.Execute(PdfTitleRule.UpToFirstColon)),
            new(Strings.CommandToggleEditPDFTitleBeforeExport, null, Strings.CommandCategoryFile, () => ToggleAskPdfTitleCommand.Execute(null)),
            new(Strings.CommandToggleAccessiblePdf, null, Strings.CommandCategoryFile, () => ToggleAccessiblePdfCommand.Execute(null)),
            new(Strings.CommandExportAsHTMLWithStyle, null, Strings.CommandCategoryFile, () => ExportWithStyleCommand.Execute(ExportFormat.Html)),
            new(Strings.CommandExportAsEPUBWithStyle, null, Strings.CommandCategoryFile, () => ExportWithStyleCommand.Execute(ExportFormat.Epub)),
            new(Strings.CommandSourceMode, "Ctrl+1", Strings.CommandCategoryView, () => SetEditorModeCommand.Execute(EditorMode.Source)),
            new(Strings.CommandSplitMode, "Ctrl+2", Strings.CommandCategoryView, () => SetEditorModeCommand.Execute(EditorMode.Split)),
            new(Strings.CommandWYSIWYGMode, "Ctrl+3", Strings.CommandCategoryView, () => SetEditorModeCommand.Execute(EditorMode.Wysiwyg)),
            new(Strings.CommandToggleTitleTOC, null, Strings.CommandCategoryView, () => ToggleShowTitleAndTocCommand.Execute(null)),
            new(Strings.CommandToggleSyncScrolling, null, Strings.CommandCategoryView, () => ToggleSyncScrollingCommand.Execute(null)),
            new(Strings.CommandToggleEditorWordWrap, null, Strings.CommandCategoryView, () => ToggleEditorWordWrapCommand.Execute(null)),
            new(Strings.CommandTogglePreviewWordWrap, null, Strings.CommandCategoryView, () => TogglePreviewWordWrapCommand.Execute(null)),
            new(Strings.CommandTogglePagePreview, null, Strings.CommandCategoryView, () => TogglePagePreviewCommand.Execute(null)),
            new(Strings.CommandToggleOutlinePane, "Ctrl+B", Strings.CommandCategoryView, () => ToggleOutlinePaneCommand.Execute(null)),
            new(Strings.CommandZoomIn, "Ctrl++", Strings.CommandCategoryView, () => ZoomInCommand.Execute(null)),
            new(Strings.CommandZoomOut, "Ctrl+-", Strings.CommandCategoryView, () => ZoomOutCommand.Execute(null)),
            new(Strings.CommandResetZoom, "Ctrl+0", Strings.CommandCategoryView, () => ZoomResetCommand.Execute(null)),
            new(Strings.CommandToggleFitWidth, null, Strings.CommandCategoryView, () => TogglePreviewFitWidthCommand.Execute(null)),
            new(Strings.CommandThemeLight, null, Strings.CommandCategoryTheme, () => SetThemeCommand.Execute(AppTheme.Light)),
            new(Strings.CommandThemeDark, null, Strings.CommandCategoryTheme, () => SetThemeCommand.Execute(AppTheme.Dark)),
            new(Strings.CommandThemeSystem, null, Strings.CommandCategoryTheme, () => SetThemeCommand.Execute(AppTheme.System)),
            new(Strings.CommandNextTab, "Ctrl+Tab", Strings.CommandCategoryNavigation, () => NextTabCommand.Execute(null)),
            new(Strings.CommandPreviousTab, "Ctrl+Shift+Tab", Strings.CommandCategoryNavigation, () => PreviousTabCommand.Execute(null)),
            new(Strings.CommandAsciiDocSyntaxReference, "F1", Strings.CommandCategoryHelp, () => ToggleSyntaxReferenceCommand.Execute(null)),
            new(Strings.CommandAbout, null, Strings.CommandCategoryHelp, () => ShowAboutCommand.Execute(null)),
        ];
    }
}
