using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;
using Ascolotl.Tests.Unit.Mocks;

namespace Ascolotl.Tests.Unit.ViewModels;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly MockFileService _fileService = new();
    private readonly MockDialogService _dialogService = new();
    private readonly MockRenderService _renderService = new();
    private readonly MockExportService _exportService = new();
    private readonly MockRecoveryStore _recoveryStore = new();
    private readonly MockSessionStore _sessionStore = new();
    private readonly MockAppLog _appLog = new();

    public MainWindowViewModelTests()
    {
        DocumentViewModel.ResetUntitledCounter();
    }

    public void Dispose()
    {
        DocumentViewModel.ResetUntitledCounter();
    }

    private MainWindowViewModel CreateViewModel()
        => new(_fileService, _dialogService, _renderService, _exportService,
               _recoveryStore, _sessionStore, _appLog, debounceMs: 0);

    // --- Constructor ---

    [Fact]
    public void Constructor_InitializesEmptyDocuments()
    {
        var vm = CreateViewModel();

        Assert.Empty(vm.Documents);
        Assert.Null(vm.SelectedDocument);
    }

    // --- New Document ---

    [Fact]
    public void NewDocument_AddsDocumentToCollection()
    {
        var vm = CreateViewModel();

        vm.NewDocumentCommand.Execute(null);

        Assert.Single(vm.Documents);
    }

    [Fact]
    public void NewDocument_SetsSelectedDocument()
    {
        var vm = CreateViewModel();

        vm.NewDocumentCommand.Execute(null);

        Assert.NotNull(vm.SelectedDocument);
        Assert.Same(vm.Documents[0], vm.SelectedDocument);
    }

    // --- Close Document ---

    [Fact]
    public async Task CloseDocument_Clean_RemovesWithoutPrompt()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        var doc = vm.SelectedDocument!;

        await vm.CloseDocumentCommand.ExecuteAsync(doc);

        Assert.Empty(vm.Documents);
        Assert.Equal(0, _dialogService.ShowSaveConfirmationCalled);
    }

    [Fact]
    public async Task CloseDocument_SelectsNextDocument()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        var first = vm.Documents[0];
        var second = vm.Documents[1];
        vm.SelectedDocument = first;

        await vm.CloseDocumentCommand.ExecuteAsync(first);

        Assert.Same(second, vm.SelectedDocument);
    }

    [Fact]
    public async Task CloseDocument_LastDocument_SetsSelectedToNull()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        var doc = vm.SelectedDocument!;

        await vm.CloseDocumentCommand.ExecuteAsync(doc);

        Assert.Null(vm.SelectedDocument);
    }

    [Fact]
    public async Task CloseDocument_Dirty_Save_SavesAndCloses()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "modified";
        _dialogService.NextResult = SaveConfirmationResult.Save;

        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument);

        Assert.Empty(vm.Documents);
        Assert.Equal(1, _fileService.SaveFileCalled);
        Assert.Equal(1, _dialogService.ShowSaveConfirmationCalled);
    }

    [Fact]
    public async Task CloseDocument_Dirty_DontSave_ClosesWithoutSaving()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "modified";
        _dialogService.NextResult = SaveConfirmationResult.DontSave;

        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument);

        Assert.Empty(vm.Documents);
        Assert.Equal(0, _fileService.SaveFileCalled);
        Assert.Equal(1, _dialogService.ShowSaveConfirmationCalled);
    }

    [Fact]
    public async Task CloseDocument_Dirty_Save_CancelledSaveAs_DoesNotClose()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "modified";
        _dialogService.NextResult = SaveConfirmationResult.Save;
        _fileService.SaveFileAsResult = null; // User cancels SaveAs picker

        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument);

        Assert.Single(vm.Documents);
        Assert.True(vm.SelectedDocument.IsDirty);
    }

    [Fact]
    public async Task CloseDocument_Dirty_Cancel_DoesNotClose()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "modified";
        _dialogService.NextResult = SaveConfirmationResult.Cancel;

        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument);

        Assert.Single(vm.Documents);
        Assert.Equal(1, _dialogService.ShowSaveConfirmationCalled);
    }

    // --- Close Tab ---

    [Fact]
    public async Task CloseTab_ClosesSelectedDocument()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);

        await vm.CloseTabCommand.ExecuteAsync(null);

        Assert.Empty(vm.Documents);
    }

    // --- Theme ---

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void SetTheme_SetsCurrentTheme(AppTheme theme)
    {
        var vm = CreateViewModel();

        vm.SetThemeCommand.Execute(theme);

        Assert.Equal(theme, vm.CurrentTheme);
    }

    // --- Open Document ---

    [Fact]
    public async Task OpenDocument_AddsDocumentWithContent()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "= Hello\n\nWorld");

        await vm.OpenDocumentCommand.ExecuteAsync(null);

        Assert.Single(vm.Documents);
        Assert.Equal("test.adoc", vm.SelectedDocument!.DisplayName);
        Assert.Equal("= Hello\n\nWorld", vm.SelectedDocument.SourceText);
        Assert.False(vm.SelectedDocument.IsDirty);
        Assert.Equal(DocumentState.Clean, vm.SelectedDocument.State);
    }

    [Fact]
    public async Task OpenDocument_Cancelled_DoesNothing()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = null;

        await vm.OpenDocumentCommand.ExecuteAsync(null);

        Assert.Empty(vm.Documents);
    }

    [Fact]
    public async Task OpenDocument_AlreadyOpen_SelectsExisting()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "content");

        await vm.OpenDocumentCommand.ExecuteAsync(null);
        await vm.OpenDocumentCommand.ExecuteAsync(null);

        Assert.Single(vm.Documents);
    }

    [Fact]
    public async Task OpenDocument_AddsToRecentFiles()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "content");

        await vm.OpenDocumentCommand.ExecuteAsync(null);

        Assert.Single(vm.RecentFiles);
        Assert.Equal("/tmp/test.adoc", vm.RecentFiles[0]);
    }

    // --- Save Document ---

    [Fact]
    public async Task SaveDocument_WritesContent()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "modified";

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(1, _fileService.SaveFileCalled);
        Assert.Equal("/tmp/test.adoc", _fileService.LastSavedPath);
        Assert.Equal("modified", _fileService.LastSavedContent);
        Assert.False(vm.SelectedDocument.IsDirty);
    }

    [Fact]
    public async Task SaveDocument_NewDocument_RedirectsToSaveAs()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "content";
        _fileService.SaveFileAsResult = "/tmp/saved.adoc";

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(1, _fileService.SaveFileAsCalled);
        Assert.Equal(0, _fileService.SaveFileCalled);
        Assert.Equal("saved.adoc", vm.SelectedDocument.DisplayName);
        Assert.False(vm.SelectedDocument.IsDirty);
    }

    // --- Save All ---

    [Fact]
    public async Task SaveAll_SavesDirtyDocumentsWithPaths()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/a.adoc", "aaa");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        _fileService.OpenFileResult = ("/tmp/b.adoc", "bbb");
        await vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.Documents[0].SourceText = "modified-a";
        vm.Documents[1].SourceText = "modified-b";

        await vm.SaveAllCommand.ExecuteAsync(null);

        Assert.Equal(2, _fileService.SaveFileCalled);
        Assert.False(vm.Documents[0].IsDirty);
        Assert.False(vm.Documents[1].IsDirty);
    }

    [Fact]
    public async Task SaveAll_SkipsCleanDocuments()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/a.adoc", "aaa");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        _fileService.OpenFileResult = ("/tmp/b.adoc", "bbb");
        await vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.Documents[0].SourceText = "modified-a";
        // Documents[1] stays clean

        await vm.SaveAllCommand.ExecuteAsync(null);

        Assert.Equal(1, _fileService.SaveFileCalled);
    }

    // --- Open Recent File ---

    [Fact]
    public async Task OpenRecentFile_OpensFileFromPath()
    {
        var vm = CreateViewModel();
        _fileService.ReadFileResult = "content from recent";

        await vm.OpenRecentFileCommand.ExecuteAsync("/tmp/recent.adoc");

        Assert.Single(vm.Documents);
        Assert.Equal("recent.adoc", vm.SelectedDocument!.DisplayName);
        Assert.Equal("content from recent", vm.SelectedDocument.SourceText);
        Assert.False(vm.SelectedDocument.IsDirty);
    }

    [Fact]
    public async Task OpenRecentFile_FileNotFound_RemovesFromList()
    {
        var vm = CreateViewModel();
        vm.LoadRecentFiles(["/tmp/missing.adoc", "/tmp/other.adoc"]);
        _fileService.ReadFileResult = null;

        await vm.OpenRecentFileCommand.ExecuteAsync("/tmp/missing.adoc");

        Assert.Single(vm.RecentFiles);
        Assert.Equal("/tmp/other.adoc", vm.RecentFiles[0]);
        Assert.Empty(vm.Documents);
    }

    [Fact]
    public async Task OpenRecentFile_AlreadyOpen_SelectsExisting()
    {
        var vm = CreateViewModel();
        _fileService.ReadFileResult = "content";
        await vm.OpenRecentFileCommand.ExecuteAsync("/tmp/recent.adoc");

        await vm.OpenRecentFileCommand.ExecuteAsync("/tmp/recent.adoc");

        Assert.Single(vm.Documents);
        Assert.Equal(1, _fileService.ReadFileCalled);
    }

    // --- Zoom ---

    [Fact]
    public void Zoom_TurnsOffFitWidth()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.TogglePreviewFitWidthCommand.Execute(null);
        Assert.True(vm.PreviewFitWidth);

        vm.ZoomInCommand.Execute(null);

        Assert.False(vm.PreviewFitWidth);
        Assert.Equal(110, vm.SelectedDocument!.ZoomLevel);
    }

    [Fact]
    public void ZoomIn_IncrementsZoomLevel()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        Assert.Equal(100, vm.SelectedDocument!.ZoomLevel);

        vm.ZoomInCommand.Execute(null);

        Assert.Equal(110, vm.SelectedDocument.ZoomLevel);
    }

    [Fact]
    public void ZoomOut_DecrementsZoomLevel()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);

        vm.ZoomOutCommand.Execute(null);

        Assert.Equal(90, vm.SelectedDocument!.ZoomLevel);
    }

    [Fact]
    public void ZoomReset_SetsTo100()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(120, vm.SelectedDocument!.ZoomLevel);

        vm.ZoomResetCommand.Execute(null);

        Assert.Equal(100, vm.SelectedDocument.ZoomLevel);
    }

    [Fact]
    public void ZoomIn_AtMax_DoesNotExceed()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.ZoomLevel = DocumentViewModel.ZoomMax;

        vm.ZoomInCommand.Execute(null);

        Assert.Equal(DocumentViewModel.ZoomMax, vm.SelectedDocument.ZoomLevel);
    }

    [Fact]
    public void ZoomOut_AtMin_DoesNotGoBelow()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.ZoomLevel = DocumentViewModel.ZoomMin;

        vm.ZoomOutCommand.Execute(null);

        Assert.Equal(DocumentViewModel.ZoomMin, vm.SelectedDocument.ZoomLevel);
    }

    [Fact]
    public void ZoomIn_NoDocument_DoesNothing()
    {
        var vm = CreateViewModel();

        vm.ZoomInCommand.Execute(null);

        Assert.Null(vm.SelectedDocument);
    }

    // --- Recent Files ---

    [Fact]
    public void AddRecentFile_PrependsToList()
    {
        var vm = CreateViewModel();

        vm.AddRecentFile("/tmp/a.adoc");
        vm.AddRecentFile("/tmp/b.adoc");

        Assert.Equal(2, vm.RecentFiles.Count);
        Assert.Equal("/tmp/b.adoc", vm.RecentFiles[0]);
        Assert.Equal("/tmp/a.adoc", vm.RecentFiles[1]);
    }

    [Fact]
    public void AddRecentFile_DuplicateMovesToTop()
    {
        var vm = CreateViewModel();

        vm.AddRecentFile("/tmp/a.adoc");
        vm.AddRecentFile("/tmp/b.adoc");
        vm.AddRecentFile("/tmp/a.adoc");

        Assert.Equal(2, vm.RecentFiles.Count);
        Assert.Equal("/tmp/a.adoc", vm.RecentFiles[0]);
        Assert.Equal("/tmp/b.adoc", vm.RecentFiles[1]);
    }

    [Fact]
    public void AddRecentFile_TrimsToMax20()
    {
        var vm = CreateViewModel();

        for (var i = 0; i < 25; i++)
            vm.AddRecentFile($"/tmp/file{i}.adoc");

        Assert.Equal(20, vm.RecentFiles.Count);
        Assert.Equal("/tmp/file24.adoc", vm.RecentFiles[0]);
    }

    [Fact]
    public void AddRecentFile_CaseInsensitiveDedup()
    {
        var vm = CreateViewModel();

        vm.AddRecentFile("/tmp/Test.adoc");
        vm.AddRecentFile("/tmp/test.adoc");

        Assert.Single(vm.RecentFiles);
        Assert.Equal("/tmp/test.adoc", vm.RecentFiles[0]);
    }

    [Fact]
    public void LoadRecentFiles_PopulatesCollection()
    {
        var vm = CreateViewModel();

        vm.LoadRecentFiles(["/tmp/a.adoc", "/tmp/b.adoc"]);

        Assert.Equal(2, vm.RecentFiles.Count);
        Assert.Equal("/tmp/a.adoc", vm.RecentFiles[0]);
        Assert.Equal("/tmp/b.adoc", vm.RecentFiles[1]);
    }

    [Fact]
    public void AddRecentFile_RaisesRecentFilesChangedEvent()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.RecentFilesChanged += () => raised = true;

        vm.AddRecentFile("/tmp/a.adoc");

        Assert.True(raised);
    }

    // --- HandleAppClosing ---

    [Fact]
    public async Task HandleAppClosing_NoDirtyDocs_ReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);

        var result = await vm.HandleAppClosingAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task HandleAppClosing_Cancel_ReturnsFalse()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "dirty";
        _dialogService.NextResult = SaveConfirmationResult.Cancel;

        var result = await vm.HandleAppClosingAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task HandleAppClosing_SaveAll_ReturnsTrue()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "dirty";
        _dialogService.NextResult = SaveConfirmationResult.Save;

        var result = await vm.HandleAppClosingAsync();

        Assert.True(result);
        Assert.Equal(1, _fileService.SaveFileCalled);
    }

    [Fact]
    public async Task HandleAppClosing_DontSave_ReturnsTrue()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "dirty";
        _dialogService.NextResult = SaveConfirmationResult.DontSave;

        var result = await vm.HandleAppClosingAsync();

        Assert.True(result);
        Assert.Equal(0, _fileService.SaveFileCalled);
    }

    // --- Editor Mode ---

    [Theory]
    [InlineData(EditorMode.Source)]
    [InlineData(EditorMode.Split)]
    public void SetEditorMode_SetsMode(EditorMode mode)
    {
        var vm = CreateViewModel();

        vm.SetEditorModeCommand.Execute(mode);

        Assert.Equal(mode, vm.EditorMode);
    }

    [Fact]
    public void EditorMode_DefaultIsSplit()
    {
        var vm = CreateViewModel();

        Assert.Equal(EditorMode.Split, vm.EditorMode);
    }

    // --- Render Pipeline ---

    [Fact]
    public async Task Render_SplitMode_SourceChange_TriggersRender()
    {
        var vm = CreateViewModel();
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        _renderService.RenderCalled = 0; // Reset from mode switch + new doc

        vm.SelectedDocument!.SourceText = "= Hello";
        await vm.PendingRenderTask;

        Assert.True(_renderService.RenderCalled > 0);
        Assert.Equal("= Hello", _renderService.LastSource);
    }

    [Fact]
    public async Task Render_SourceMode_DoesNotTriggerRender()
    {
        var vm = CreateViewModel();
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
        vm.NewDocumentCommand.Execute(null);
        _renderService.RenderCalled = 0;

        vm.SelectedDocument!.SourceText = "= Hello";
        await Task.Delay(50); // Give any async work time to run

        Assert.Equal(0, _renderService.RenderCalled);
    }

    [Fact]
    public async Task Render_SwitchToSplit_TriggersRender()
    {
        var vm = CreateViewModel();
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Hello";
        _renderService.RenderCalled = 0; // Reset — Source mode, no render expected

        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        await vm.PendingRenderTask;

        Assert.True(_renderService.RenderCalled > 0);
    }

    [Fact]
    public async Task Render_Success_UpdatesDocumentProperties()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Success("<p>rendered</p>");
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        await vm.PendingRenderTask;

        Assert.Equal("<p>rendered</p>", vm.SelectedDocument!.RenderedHtml);
        Assert.True(vm.SelectedDocument.LastRenderSucceeded);
        Assert.Null(vm.SelectedDocument.RenderError);
        Assert.False(vm.SelectedDocument.IsRendering);
    }

    [Fact]
    public async Task Render_Failure_SetsErrorState()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Failure("asciidoctor not found");
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        await vm.PendingRenderTask;

        Assert.False(vm.SelectedDocument!.LastRenderSucceeded);
        Assert.Equal("asciidoctor not found", vm.SelectedDocument.RenderError);
        Assert.Equal(string.Empty, vm.SelectedDocument.RenderedHtml);
    }

    [Fact]
    public async Task Render_Failure_DoesNotBreakEditing()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Failure("error");
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        await vm.PendingRenderTask;

        // Editing should still work after render failure
        vm.SelectedDocument!.SourceText = "new content";

        Assert.Equal("new content", vm.SelectedDocument.SourceText);
        Assert.True(vm.SelectedDocument.IsDirty);
    }

    [Fact]
    public async Task Render_PreservesSourceText()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Success("<p>html</p>");
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= My Document";
        await vm.PendingRenderTask;

        // Source text must remain unchanged after rendering
        Assert.Equal("= My Document", vm.SelectedDocument.SourceText);
    }

    [Fact]
    public async Task Render_TabSwitch_TriggersRenderForNewDoc()
    {
        var vm = CreateViewModel();
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "doc1";
        await vm.PendingRenderTask;

        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "doc2";
        _renderService.RenderCalled = 0;

        // Switch back to first doc
        vm.SelectedDocument = vm.Documents[0];
        await vm.PendingRenderTask;

        Assert.True(_renderService.RenderCalled > 0);
    }

    [Fact]
    public async Task Render_StatusText_UpdatesOnSuccess()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Success("<p>ok</p>");
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        await vm.PendingRenderTask;

        Assert.Equal("Render complete", vm.StatusText);
    }

    [Fact]
    public async Task Render_StatusText_UpdatesOnFailure()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Failure("parse error");
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        await vm.PendingRenderTask;

        Assert.Contains("parse error", vm.StatusText);
    }

    // --- Export as PDF ---

    [Fact]
    public async Task ExportAsPdf_NoDocument_DoesNothing()
    {
        var vm = CreateViewModel();

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(0, _exportService.ExportCalled);
        Assert.Equal(0, _fileService.PickSavePathCalled);
    }

    [Fact]
    public async Task ExportAsPdf_UserCancelsPicker_DoesNotExport()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Hello";
        _fileService.PickSavePathResult = null;

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(1, _fileService.PickSavePathCalled);
        Assert.Equal(0, _exportService.ExportCalled);
    }

    [Fact]
    public async Task ExportAsPdf_Success_UpdatesStatus()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/doc.adoc", "= Hello");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        await vm.PendingRenderTask; // Let debounce render finish first
        _fileService.PickSavePathResult = "/tmp/doc.pdf";
        _exportService.ExportResult = ExportResult.Success();

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(1, _exportService.ExportCalled);
        Assert.Equal("= Hello", _exportService.LastSource);
        Assert.Equal("/tmp/doc.pdf", _exportService.LastOutputPath);
        Assert.Equal(ExportFormat.Pdf, _exportService.LastFormat);
        Assert.Contains("doc.pdf", vm.StatusText);
    }

    [Fact]
    public async Task ExportAsPdf_Failure_ShowsError()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Hello";
        _fileService.PickSavePathResult = "/tmp/out.pdf";
        _exportService.ExportResult = ExportResult.Failure("asciidoctor-pdf not found");

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Contains("asciidoctor-pdf not found", vm.StatusText);
    }

    [Fact]
    public async Task ExportAsPdf_PassesBasePath()
    {
        var vm = CreateViewModel();
        var docPath = Path.Combine("tmp", "docs", "guide.adoc");
        _fileService.OpenFileResult = (docPath, "= Guide");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        _fileService.PickSavePathResult = Path.Combine("tmp", "out.pdf");
        _exportService.ExportResult = ExportResult.Success();

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(Path.Combine("tmp", "docs"), _exportService.LastBasePath);
    }

    [Fact]
    public async Task ExportAsPdf_UnsavedDoc_NullBasePath()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Hello";
        _fileService.PickSavePathResult = "/tmp/out.pdf";
        _exportService.ExportResult = ExportResult.Success();

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Null(_exportService.LastBasePath);
    }

    [Fact]
    public async Task LoadStyle_PassesThemeToExport()
    {
        var vm = CreateViewModel();
        var theme = Path.Combine("tmp", "corp-pdf-theme.yml");
        _fileService.PickOpenFilePathResult = theme;
        await vm.LoadStyleCommand.ExecuteAsync(null);
        vm.NewDocumentCommand.Execute(null);
        _fileService.PickSavePathResult = Path.Combine("tmp", "out.pdf");
        _exportService.ExportResult = ExportResult.Success();

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.True(vm.HasStyle);
        Assert.Equal(theme, _exportService.LastStylePath);
    }

    [Fact]
    public async Task LoadStyle_Cancelled_KeepsCurrentTheme()
    {
        var vm = CreateViewModel();
        vm.StylePath = "kept.yml";
        _fileService.PickOpenFilePathResult = null;

        await vm.LoadStyleCommand.ExecuteAsync(null);

        Assert.Equal("kept.yml", vm.StylePath);
    }

    [Fact]
    public void ResetStyle_ClearsTheme()
    {
        var vm = CreateViewModel();
        vm.StylePath = "corp.yml";

        vm.ResetStyleCommand.Execute(null);

        Assert.Null(vm.StylePath);
        Assert.False(vm.HasStyle);
    }

    [Fact]
    public void StyleMenuHeader_NamesActiveTheme()
    {
        var vm = CreateViewModel();
        Assert.Equal("Current: Built-in", vm.StyleMenuHeader);

        vm.StylePath = Path.Combine("themes", "corp_pdf-theme.yml");

        Assert.Equal("Current: corp__pdf-theme.yml", vm.StyleMenuHeader);
    }

    [Fact]
    public async Task ExportWithStyle_UsesPickedStyleOnce_KeepsDefault()
    {
        var vm = CreateViewModel();
        vm.StylePath = "default.yml";
        vm.NewDocumentCommand.Execute(null);
        _fileService.PickOpenFilePathResult = "one-off.yml";
        _fileService.PickSavePathResult = Path.Combine("tmp", "out.html");
        _exportService.ExportResult = ExportResult.Success();

        await vm.ExportWithStyleCommand.ExecuteAsync(ExportFormat.Html);

        Assert.Equal("one-off.yml", _exportService.LastStylePath);
        Assert.Equal(ExportFormat.Html, _exportService.LastFormat);
        Assert.Equal("default.yml", vm.StylePath);
    }

    [Fact]
    public async Task ExportWithStyle_StylePickerCancelled_DoesNotExport()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        _fileService.PickOpenFilePathResult = null;

        await vm.ExportWithStyleCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(0, _exportService.ExportCalled);
    }

    // --- PDF title ---

    private MainWindowViewModel VmWithTitledDocument(string title)
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.RenderedDocument = new AdocNet.Ast.DocumentNode { Title = title };
        _fileService.PickSavePathResult = Path.Combine("tmp", "out.pdf");
        _exportService.ExportResult = ExportResult.Success();
        return vm;
    }

    [Fact]
    public async Task ExportPdf_DefaultRule_StopsAtFirstColon()
    {
        var vm = VmWithTitledDocument("Manual: The long subtitle");
        Assert.True(vm.IsPdfTitleUpToColon);
        Assert.Equal(PdfTitleRule.UpToFirstColon, new AppSettings().PdfTitleRule);

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal("Manual", _exportService.LastPdfTitle);
        Assert.Equal(0, _dialogService.AskPdfTitleCalled);
    }

    [Fact]
    public async Task ExportPdf_FullTitleRule_KeepsSubtitle()
    {
        var vm = VmWithTitledDocument("Manual: The long subtitle");
        vm.SetPdfTitleRuleCommand.Execute(PdfTitleRule.FullTitle);

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal("Manual: The long subtitle", _exportService.LastPdfTitle);
    }

    [Fact]
    public async Task ExportPdf_AskTitle_ShowsRuleResultAndUsesEdit()
    {
        var vm = VmWithTitledDocument("Manual: The long subtitle");
        vm.ToggleAskPdfTitleCommand.Execute(null);
        _dialogService.PdfTitleAnswer = _ => "Edited";

        await vm.ExportCommand.ExecuteAsync(ExportFormat.PdfLandscape);

        Assert.Equal("Manual", _dialogService.LastPdfTitleShown);
        Assert.Equal("Edited", _exportService.LastPdfTitle);
    }

    [Fact]
    public async Task ExportPdf_AskTitleCancelled_DoesNotExport()
    {
        var vm = VmWithTitledDocument("Manual");
        vm.AskPdfTitle = true;
        _dialogService.PdfTitleAnswer = _ => null;

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(0, _exportService.ExportCalled);
    }

    [Fact]
    public async Task ExportPdf_AskTitleEmptied_KeepsDocumentTitle()
    {
        var vm = VmWithTitledDocument("Manual: Sub");
        vm.AskPdfTitle = true;
        _dialogService.PdfTitleAnswer = _ => "  ";

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(1, _exportService.ExportCalled);
        Assert.Null(_exportService.LastPdfTitle);
    }

    [Fact]
    public async Task Export_Failure_ShowsErrorDialog()
    {
        var vm = VmWithTitledDocument("Manual");
        _exportService.ExportResult = ExportResult.Failure("out.pdf is open in another program.");

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        var (title, message) = Assert.Single(_dialogService.Errors);
        Assert.Equal("Export Failed", title);
        Assert.Equal("out.pdf is open in another program.", message);
        Assert.Equal("out.pdf is open in another program.", vm.StatusText);
    }

    [Fact]
    public async Task Export_Success_ShowsNoDialog()
    {
        var vm = VmWithTitledDocument("Manual");

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Empty(_dialogService.Errors);
    }

    [Fact]
    public async Task ExportHtml_NeverAsksPdfTitle()
    {
        var vm = VmWithTitledDocument("Manual: Sub");
        vm.AskPdfTitle = true;
        _fileService.PickSavePathResult = Path.Combine("tmp", "out.html");

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Html);

        Assert.Equal(0, _dialogService.AskPdfTitleCalled);
        Assert.Null(_exportService.LastPdfTitle);
    }

    // --- Reopen Closed Tab ---

    [Fact]
    public async Task ReopenClosedTab_ReopensLastClosed()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "content");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument!);
        Assert.Empty(vm.Documents);

        _fileService.ReadFileResult = "content";
        await vm.ReopenClosedTabCommand.ExecuteAsync(null);

        Assert.Single(vm.Documents);
        Assert.Equal("test.adoc", vm.SelectedDocument!.DisplayName);
    }

    [Fact]
    public async Task ReopenClosedTab_EmptyStack_DoesNothing()
    {
        var vm = CreateViewModel();

        await vm.ReopenClosedTabCommand.ExecuteAsync(null);

        Assert.Empty(vm.Documents);
    }

    [Fact]
    public async Task ReopenClosedTab_UntitledDoc_NotInStack()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument!);

        _fileService.ReadFileResult = "content";
        await vm.ReopenClosedTabCommand.ExecuteAsync(null);

        // Untitled (no FilePath) should not be in the closed tab stack
        Assert.Empty(vm.Documents);
    }

    // --- Tab Switching ---

    [Fact]
    public void NextTab_CyclesForward()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument = vm.Documents[0];

        vm.NextTabCommand.Execute(null);

        Assert.Same(vm.Documents[1], vm.SelectedDocument);
    }

    [Fact]
    public void NextTab_WrapsAround()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument = vm.Documents[1]; // last

        vm.NextTabCommand.Execute(null);

        Assert.Same(vm.Documents[0], vm.SelectedDocument);
    }

    [Fact]
    public void PreviousTab_CyclesBackward()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument = vm.Documents[1];

        vm.PreviousTabCommand.Execute(null);

        Assert.Same(vm.Documents[0], vm.SelectedDocument);
    }

    [Fact]
    public void PreviousTab_WrapsAround()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument = vm.Documents[0]; // first

        vm.PreviousTabCommand.Execute(null);

        Assert.Same(vm.Documents[1], vm.SelectedDocument);
    }

    [Fact]
    public void NextTab_SingleDoc_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        var doc = vm.SelectedDocument!;

        vm.NextTabCommand.Execute(null);

        Assert.Same(doc, vm.SelectedDocument);
    }

    // --- Outline ---

    [Fact]
    public void ToggleOutlinePane_TogglesFlag()
    {
        var vm = CreateViewModel();
        Assert.False(vm.ShowOutlinePane);

        vm.ToggleOutlinePaneCommand.Execute(null);

        Assert.True(vm.ShowOutlinePane);

        vm.ToggleOutlinePaneCommand.Execute(null);

        Assert.False(vm.ShowOutlinePane);
    }

    [Fact]
    public async Task Render_PopulatesOutlineItems()
    {
        var vm = CreateViewModel();
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Title\n\n== Chapter One\n\n== Chapter Two";
        await vm.PendingRenderTask;

        Assert.Equal(3, vm.OutlineItems.Count);
        Assert.Equal("Title", vm.OutlineItems[0].Title);
        Assert.Equal("Chapter One", vm.OutlineItems[1].Title);
        Assert.Equal("Chapter Two", vm.OutlineItems[2].Title);
    }

    [Fact]
    public void NavigateToOutlineItem_RaisesEvent()
    {
        var vm = CreateViewModel();
        int? navigatedLine = null;
        vm.NavigateToLineRequested += line => navigatedLine = line;

        var item = new Ascolotl.Core.Models.OutlineItem("Test", 2, 10);
        vm.NavigateToOutlineItemCommand.Execute(item);

        Assert.Equal(10, navigatedLine);
    }

    // --- Command Palette ---

    [Fact]
    public void CommandPalette_IsInitialized()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.CommandPalette);
        Assert.True(vm.CommandPalette.AllCommands.Count > 0);
    }

    [Fact]
    public void ToggleCommandPalette_OpensAndCloses()
    {
        var vm = CreateViewModel();

        vm.ToggleCommandPaletteCommand.Execute(null);
        Assert.True(vm.CommandPalette.IsOpen);

        vm.ToggleCommandPaletteCommand.Execute(null);
        Assert.False(vm.CommandPalette.IsOpen);
    }

    // --- Autosave ---

    [Fact]
    public void RunAutosave_SavesSnapshotsForDirtyDocs()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "dirty content";

        vm.RunAutosave();

        Assert.Equal(1, _recoveryStore.SaveSnapshotCalled);
        Assert.Equal("dirty content", _recoveryStore.LastSavedSourceText);
    }

    [Fact]
    public void RunAutosave_SkipsCleanDocs()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/clean.adoc", "clean");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.RunAutosave();

        Assert.Equal(0, _recoveryStore.SaveSnapshotCalled);
    }

    [Fact]
    public void EmergencyRecoverySave_SavesDirtyDocs()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "emergency content";

        vm.EmergencyRecoverySave();

        Assert.Equal(1, _recoveryStore.SaveSnapshotCalled);
    }

    // --- Recovery ---

    [Fact]
    public async Task CheckAndRecover_NoEntries_ReturnsFalse()
    {
        var vm = CreateViewModel();

        var result = await vm.CheckAndRecoverAsync();

        Assert.False(result);
        Assert.Equal(0, _dialogService.ShowRecoveryDialogCalled);
    }

    [Fact]
    public async Task CheckAndRecover_UserAccepts_RecoversDocs()
    {
        var vm = CreateViewModel();
        _recoveryStore.SetEntries([
            new() { DocumentId = "doc1", OriginalFilePath = "/tmp/test.adoc", DisplayName = "test.adoc", Timestamp = DateTime.UtcNow }
        ]);
        _recoveryStore.SetSnapshot("doc1", "recovered content");
        _dialogService.NextRecoveryResult = true;

        var result = await vm.CheckAndRecoverAsync();

        Assert.True(result);
        Assert.Single(vm.Documents);
        Assert.Equal("recovered content", vm.Documents[0].SourceText);
        Assert.True(vm.Documents[0].IsDirty);
        Assert.True(vm.Documents[0].IsRecovered);
        // Recovery data stays on disk as safety net — not cleared until
        // save or clean shutdown
        Assert.Equal(0, _recoveryStore.ClearAllCalled);
    }

    [Fact]
    public async Task CheckAndRecover_UserDeclines_ClearsRecovery()
    {
        var vm = CreateViewModel();
        _recoveryStore.SetEntries([
            new() { DocumentId = "doc1", DisplayName = "test.adoc", Timestamp = DateTime.UtcNow }
        ]);
        _dialogService.NextRecoveryResult = false;

        var result = await vm.CheckAndRecoverAsync();

        Assert.False(result);
        Assert.Empty(vm.Documents);
        Assert.Equal(1, _recoveryStore.ClearAllCalled);
    }

    // --- Session ---

    [Fact]
    public void SaveSession_PersistsOpenDocs()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/a.adoc", "content");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.SaveSession();

        Assert.Equal(1, _sessionStore.SaveCalled);
        Assert.NotNull(_sessionStore.LastSavedState);
        Assert.Single(_sessionStore.LastSavedState!.OpenDocuments);
        Assert.Equal("/tmp/a.adoc", _sessionStore.LastSavedState.OpenDocuments[0].FilePath);
    }

    [Fact]
    public void SaveSession_SkipsUntitledDocs()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);

        vm.SaveSession();

        Assert.Equal(1, _sessionStore.SaveCalled);
        Assert.Empty(_sessionStore.LastSavedState!.OpenDocuments);
    }

    [Fact]
    public void SaveSession_IncludesSelectedIndex()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/a.adoc", "a");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);
        _fileService.OpenFileResult = ("/tmp/b.adoc", "b");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument = vm.Documents[1];

        vm.SaveSession();

        Assert.Equal(1, _sessionStore.LastSavedState!.SelectedIndex);
    }

    // --- Cleanup ---

    [Fact]
    public void CleanupRecoveryOnShutdown_RemovesSnapshotsForOpenDocs()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.NewDocumentCommand.Execute(null);

        vm.CleanupRecoveryOnShutdown();

        Assert.Equal(2, _recoveryStore.RemoveSnapshotCalled);
    }

    // --- DocumentViewModel ---

    [Fact]
    public void DocumentViewModel_HasDocumentId()
    {
        var doc = DocumentViewModel.CreateNew();
        Assert.NotNull(doc.DocumentId);
        Assert.NotEmpty(doc.DocumentId);
    }

    [Fact]
    public void DocumentViewModel_RecoveredTabTitle()
    {
        var doc = new DocumentViewModel { DisplayName = "test.adoc", IsRecovered = true };
        doc.IsDirty = true;

        Assert.Contains("[Recovered]", doc.TabTitle);
        Assert.Contains("test.adoc", doc.TabTitle);
        Assert.Contains("*", doc.TabTitle);
    }

    // --- Save removes recovery ---

    [Fact]
    public async Task Save_RemovesRecoverySnapshot()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "modified";

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.True(_recoveryStore.RemoveSnapshotCalled > 0);
    }
}
