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

    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        DocumentViewModel.ResetUntitledCounter();
        foreach (var dir in _tempDirs)
            try { Directory.Delete(dir, recursive: true); } catch { }
        foreach (var path in _tempFiles)
            File.Delete(path);
    }

    private MainWindowViewModel CreateViewModel()
        => new(_fileService, _dialogService, _renderService, _exportService,
               _recoveryStore, _sessionStore, _appLog, debounceMs: 0);

    // --- Constructor ---

    [Fact]
    public async Task ImportPlainText_OpensUnsavedTabNamedAfterTheSource()
    {
        var vm = CreateViewModel();
        _fileService.PickOpenFilePathResult = Path.Combine("in", "notes.txt");
        _fileService.ReadFileResult = "Hello\n- one\n";

        await vm.ImportCommand.ExecuteAsync(ImportFormat.PlainText);

        var doc = vm.SelectedDocument!;
        Assert.Equal("notes.adoc", doc.DisplayName);
        Assert.Null(doc.FilePath);
        Assert.True(doc.IsDirty);
        Assert.Equal("= notes\n\nHello\n\n* one\n", doc.SourceText);
        Assert.Equal(0, _fileService.SaveFileCalled + _fileService.SaveFileAsCalled);
    }

    [Fact]
    public async Task ImportPlainText_UnreadableFile_ShowsErrorDialog()
    {
        var vm = CreateViewModel();
        _fileService.PickOpenFilePathResult = "gone.txt";
        _fileService.ReadFileResult = null;

        await vm.ImportCommand.ExecuteAsync(ImportFormat.PlainText);

        Assert.Equal("Import Failed", Assert.Single(_dialogService.Errors).Title);
        Assert.Empty(vm.Documents);
    }

    private const string HtmlWithImage =
        "<html><body><h1>Page</h1><p>Hi</p><img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==\"></body></html>";

    private async Task<(MainWindowViewModel Vm, string Dir)> ImportHtmlWithImage()
    {
        var dir = Directory.CreateTempSubdirectory("ascolotl-import-").FullName;
        _tempDirs.Add(dir);
        var html = Path.Combine(dir, "page.html");
        File.WriteAllText(html, HtmlWithImage);
        var vm = new MainWindowViewModel(_fileService, _dialogService, _renderService, _exportService,
            _recoveryStore, _sessionStore, _appLog, debounceMs: 0, importService: new Ascolotl.Export.DocumentImportService());
        _fileService.PickOpenFilePathResult = html;
        await vm.ImportCommand.ExecuteAsync(ImportFormat.Html);
        return (vm, dir);
    }

    [Fact]
    public async Task ImportHtml_OpensUnsavedTab_ImagesWaitInPrivateFolder()
    {
        var (vm, dir) = await ImportHtmlWithImage();

        var doc = vm.SelectedDocument!;
        Assert.Equal("page.adoc", doc.DisplayName);
        Assert.Null(doc.FilePath);
        Assert.Contains("image::page-media/", doc.SourceText);
        Assert.Equal(doc.AssetDirectory, doc.BaseDirectory);
        Assert.Single(Directory.GetFiles(Path.Combine(doc.AssetDirectory!, "page-media")));
        Assert.Equal(["page.html"], Directory.GetFileSystemEntries(dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ImportHtml_FirstSave_CopiesImagesBesideTheFile()
    {
        var (vm, dir) = await ImportHtmlWithImage();
        var doc = vm.SelectedDocument!;
        var assets = doc.AssetDirectory!;
        _fileService.SaveFileAsResult = Path.Combine(dir, "saved.adoc");

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(dir, "saved.adoc"), doc.FilePath);
        Assert.Single(Directory.GetFiles(Path.Combine(dir, "page-media")));
        Assert.Null(doc.AssetDirectory);
        Assert.False(Directory.Exists(assets));
    }

    [Fact]
    public async Task ImportHtml_FirstSave_KeepsAnExistingImageOfTheSameName()
    {
        var (vm, dir) = await ImportHtmlWithImage();
        var image = Path.GetFileName(Directory.GetFiles(Path.Combine(vm.SelectedDocument!.AssetDirectory!, "page-media")).Single());
        Directory.CreateDirectory(Path.Combine(dir, "page-media"));
        File.WriteAllText(Path.Combine(dir, "page-media", image), "mine");
        _fileService.SaveFileAsResult = Path.Combine(dir, "page.adoc");

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal("mine", File.ReadAllText(Path.Combine(dir, "page-media", image)));
        Assert.Equal("Images Not Replaced", Assert.Single(_dialogService.Errors).Title);
    }

    [Fact]
    public async Task ImportHtml_ClosedWithoutSaving_DeletesThePrivateFolder()
    {
        var (vm, _) = await ImportHtmlWithImage();
        var assets = vm.SelectedDocument!.AssetDirectory!;
        _dialogService.NextResult = SaveConfirmationResult.DontSave;

        await vm.CloseDocumentCommand.ExecuteAsync(vm.SelectedDocument);

        Assert.False(Directory.Exists(assets));
    }

    [Fact]
    public void EmptyState_HiddenWhileStartupOpensDocuments()
    {
        var vm = CreateViewModel();
        Assert.True(vm.HasNoDocuments);

        vm.OpeningDocuments = true;
        Assert.False(vm.HasNoDocuments);

        vm.OpeningDocuments = false;
        Assert.True(vm.HasNoDocuments);
    }

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
    public async Task OpenedFromOutside_StartsInReadView_AndAnOpenDocumentKeepsItsView()
    {
        var vm = CreateViewModel();
        _fileService.ReadFileResult = "Text";

        await vm.OpenFileByPathAsync("/tmp/a.adoc", EditorMode.Read); // as from Explorer
        Assert.Equal(EditorMode.Read, vm.SelectedDocument!.EditorMode);
        Assert.Equal(EditorMode.Read, vm.EditorMode);

        vm.EditorMode = EditorMode.Split; // chosen to edit
        await vm.OpenFileByPathAsync("/tmp/a.adoc", EditorMode.Read); // opened again: already open
        Assert.Equal(EditorMode.Split, vm.EditorMode);

        await vm.OpenFileByPathAsync("/tmp/b.adoc"); // from inside the app: as before
        Assert.Equal(EditorMode.Wysiwyg, vm.SelectedDocument!.EditorMode);
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
        var (title, message) = Assert.Single(_dialogService.Errors);
        Assert.Equal("Ascolotl — File Not Found", title);
        Assert.Contains("/tmp/missing.adoc", message);
        Assert.Contains("removed from Recent Files", message);
    }

    [Fact]
    public async Task OpenRecentFile_ExistingButUnreadable_StaysInList_AndSaysWhy()
    {
        var path = Path.GetTempFileName();
        try
        {
            var vm = CreateViewModel();
            vm.LoadRecentFiles([path]);
            _fileService.ReadFileResult = null;

            await vm.OpenRecentFileCommand.ExecuteAsync(path);

            Assert.Equal([path], vm.RecentFiles);
            var (title, message) = Assert.Single(_dialogService.Errors);
            Assert.Equal("Ascolotl — Could Not Open File", title);
            Assert.Contains(path, message);
        }
        finally
        {
            File.Delete(path);
        }
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

    [Theory]
    [InlineData(117, true, 120)]
    [InlineData(117, false, 110)]
    [InlineData(120, true, 130)]
    [InlineData(120, false, 110)]
    public void ZoomSteps_SnapToMultiplesOfTen(int start, bool zoomIn, int expected)
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.ZoomLevel = start;

        (zoomIn ? vm.ZoomInCommand : vm.ZoomOutCommand).Execute(null);

        Assert.Equal(expected, vm.SelectedDocument.ZoomLevel);
    }

    [Fact]
    public void ZoomBy_SmallStepsAddUp()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);

        // A touchpad pinch: many tiny factors, each below one percent.
        for (int i = 0; i < 20; i++)
            vm.ZoomBy(1.004);

        Assert.Equal(108, vm.SelectedDocument!.ZoomLevel); // 100 * 1.004^20 = 108.3
    }

    [Fact]
    public void ZoomBy_ClampsToRange_AndTurnsOffFitWidth()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.PreviewFitWidth = true;

        vm.ZoomBy(100);

        Assert.Equal(DocumentViewModel.ZoomMax, vm.SelectedDocument!.ZoomLevel);
        Assert.False(vm.PreviewFitWidth);
        vm.ZoomBy(0.001);
        Assert.Equal(DocumentViewModel.ZoomMin, vm.SelectedDocument.ZoomLevel);
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
    public void AddRecentFile_DedupsAsTheFileSystemComparesNames()
    {
        var vm = CreateViewModel();

        vm.AddRecentFile("/tmp/Test.adoc");
        vm.AddRecentFile("/tmp/test.adoc");

        Assert.Equal(OperatingSystem.IsLinux() ? 2 : 1, vm.RecentFiles.Count); // two files on Linux
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
    public void EditorMode_IsPerTab_AndANewTabStartsInWysiwyg()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        var a = vm.SelectedDocument!;
        Assert.Equal(EditorMode.Wysiwyg, vm.EditorMode);
        vm.SetEditorModeCommand.Execute(EditorMode.Source);

        vm.NewDocumentCommand.Execute(null);
        Assert.Equal(EditorMode.Wysiwyg, vm.EditorMode);
        vm.SelectedDocument = a;
        Assert.Equal(EditorMode.Source, vm.EditorMode);
        Assert.True(vm.IsSourceMode);
    }

    // --- Render Pipeline ---

    [Fact]
    public async Task Render_SplitMode_SourceChange_TriggersRender()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        _renderService.RenderCalled = 0; // Reset from mode switch + new doc

        vm.SelectedDocument!.SourceText = "= Hello";
        await vm.PendingRenderTask;

        Assert.True(_renderService.RenderCalled > 0);
        Assert.Equal("= Hello", _renderService.LastSource);
    }

    // On the UI thread, as in the app: the coalescing relies on the dispatcher
    // running the render after the synchronous selection changes.
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Render_SelectionChangedTwiceInOneStep_RendersOnlyTheLastDocument()
    {
        // Session restore: the tab strip selects the first tab, then the saved one.
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.NewDocumentCommand.Execute(null);
        vm.Documents[0].SourceText = "= First";
        vm.Documents[1].SourceText = "= Second";
        await vm.PendingRenderTask;
        _renderService.RenderCalled = 0;

        vm.SelectedDocument = vm.Documents[0];
        vm.SelectedDocument = vm.Documents[1];
        await vm.PendingRenderTask;

        Assert.Equal(1, _renderService.RenderCalled);
        Assert.Equal("= Second", _renderService.LastSource);
    }

    [Fact]
    public async Task Render_SourceMode_DoesNotTriggerRender()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
        await vm.PendingRenderTask; // the new tab's WYSIWYG render
        _renderService.RenderCalled = 0;

        vm.SelectedDocument!.SourceText = "= Hello";
        await Task.Delay(50); // Give any async work time to run

        Assert.Equal(0, _renderService.RenderCalled);
    }

    [Fact]
    public async Task Render_SwitchToSplit_TriggersRender()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
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
        var rendered = AdocNet.Parser.AdocParser.Parse("= Rendered").Document;
        _renderService.NextResult = RenderResult.Success(rendered);
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        await vm.PendingRenderTask;

        Assert.Same(rendered, vm.SelectedDocument!.RenderedDocument);
        Assert.True(vm.SelectedDocument.LastRenderSucceeded);
        Assert.Null(vm.SelectedDocument.RenderError);
    }

    [Fact]
    public async Task Render_Failure_SetsErrorState()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Failure("asciidoctor not found");
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        await vm.PendingRenderTask;

        Assert.False(vm.SelectedDocument!.LastRenderSucceeded);
        Assert.Equal("asciidoctor not found", vm.SelectedDocument.RenderError);
        Assert.Null(vm.SelectedDocument.RenderedDocument);
    }

    [Fact]
    public async Task Render_Failure_DoesNotBreakEditing()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Failure("error");
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
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
        _renderService.NextResult = RenderResult.Success();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        vm.SelectedDocument!.SourceText = "= My Document";
        await vm.PendingRenderTask;

        // Source text must remain unchanged after rendering
        Assert.Equal("= My Document", vm.SelectedDocument.SourceText);
    }

    [Fact]
    public async Task Render_TabSwitch_TriggersRenderForNewDoc()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
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
        _renderService.NextResult = RenderResult.Success();
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
        await vm.PendingRenderTask;

        Assert.Equal("Render complete", vm.StatusText);
    }

    [Fact]
    public async Task Render_StatusText_UpdatesOnFailure()
    {
        var vm = CreateViewModel();
        _renderService.NextResult = RenderResult.Failure("parse error");
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Source);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportAsPdf_FollowsTheAccessibleSetting_AndReportsMissingAltText(bool accessible)
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Hello";
        if (accessible)
            vm.ToggleAccessiblePdfCommand.Execute(null);
        _fileService.PickSavePathResult = "/tmp/out.pdf";
        _exportService.ExportResult = ExportResult.Success(accessible ? ["pic.png: no alt text"] : null);

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(accessible, _exportService.LastAccessiblePdf);
        Assert.Equal(accessible, vm.StatusText.Contains("1 image(s) without alt text"));
    }

    [Fact]
    public async Task ExportAsPdf_Failure_ShowsError()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.SelectedDocument!.SourceText = "= Hello";
        await vm.PendingRenderTask; // or its "Render complete" can replace the error status
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

    private async Task<MainWindowViewModel> VmWithTitledDocument(string title)
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        await vm.PendingRenderTask; // or the new document's render replaces the title set below
        vm.SelectedDocument!.RenderedDocument = new AdocNet.Ast.DocumentNode { Title = title };
        _fileService.PickSavePathResult = Path.Combine("tmp", "out.pdf");
        _exportService.ExportResult = ExportResult.Success();
        return vm;
    }

    [Fact]
    public async Task ExportPdf_DefaultRule_StopsAtFirstColon()
    {
        var vm = await VmWithTitledDocument("Manual: The long subtitle");
        Assert.True(vm.IsPdfTitleUpToColon);
        Assert.Equal(PdfTitleRule.UpToFirstColon, new AppSettings().PdfTitleRule);

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal("Manual", _exportService.LastPdfTitle);
        Assert.Equal(0, _dialogService.AskPdfTitleCalled);
    }

    [Fact]
    public async Task ExportPdf_FullTitleRule_KeepsSubtitle()
    {
        var vm = await VmWithTitledDocument("Manual: The long subtitle");
        vm.SetPdfTitleRuleCommand.Execute(PdfTitleRule.FullTitle);

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal("Manual: The long subtitle", _exportService.LastPdfTitle);
    }

    [Fact]
    public async Task ExportPdf_AskTitle_ShowsRuleResultAndUsesEdit()
    {
        var vm = await VmWithTitledDocument("Manual: The long subtitle");
        vm.ToggleAskPdfTitleCommand.Execute(null);
        _dialogService.PdfTitleAnswer = _ => "Edited";

        await vm.ExportCommand.ExecuteAsync(ExportFormat.PdfLandscape);

        Assert.Equal("Manual", _dialogService.LastPdfTitleShown);
        Assert.Equal("Edited", _exportService.LastPdfTitle);
    }

    [Fact]
    public async Task ExportPdf_AskTitleCancelled_DoesNotExport()
    {
        var vm = await VmWithTitledDocument("Manual");
        vm.AskPdfTitle = true;
        _dialogService.PdfTitleAnswer = _ => null;

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(0, _exportService.ExportCalled);
    }

    [Fact]
    public async Task ExportPdf_AskTitleEmptied_KeepsDocumentTitle()
    {
        var vm = await VmWithTitledDocument("Manual: Sub");
        vm.AskPdfTitle = true;
        _dialogService.PdfTitleAnswer = _ => "  ";

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Equal(1, _exportService.ExportCalled);
        Assert.Null(_exportService.LastPdfTitle);
    }

    [Fact]
    public async Task Export_Failure_ShowsErrorDialog()
    {
        var vm = await VmWithTitledDocument("Manual");
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
        var vm = await VmWithTitledDocument("Manual");

        await vm.ExportCommand.ExecuteAsync(ExportFormat.Pdf);

        Assert.Empty(_dialogService.Errors);
    }

    [Fact]
    public async Task ExportHtml_NeverAsksPdfTitle()
    {
        var vm = await VmWithTitledDocument("Manual: Sub");
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
        vm.NewDocumentCommand.Execute(null);
        vm.SetEditorModeCommand.Execute(EditorMode.Split);
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
    public void ProjectFolder_ListsItsDocuments_PastFoldersItCannotRead()
    {
        var root = Directory.CreateTempSubdirectory("ascolotl-project-").FullName;
        _tempDirs.Add(root);
        File.WriteAllText(Path.Combine(root, "a.adoc"), "");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "sub", "b.md"), "");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, ".git", "c.md"), "");
        var locked = Directory.CreateDirectory(Path.Combine(root, "locked")).FullName;
        File.WriteAllText(Path.Combine(locked, "d.adoc"), "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var vm = CreateViewModel();
            vm.LoadProjectFolder(root);

            Assert.Equal(string.Format(Ascolotl.Core.Resources.Strings.StatusFolderOpened, Path.GetFileName(root)), vm.StatusText);
            var names = vm.ProjectRoots.SelectMany(n => n.Children.Select(c => c.Name).Prepend(n.Name)).ToList();
            Assert.Contains("a.adoc", names);
            Assert.Contains("b.md", names);
            Assert.DoesNotContain("c.md", names);
        }
        finally
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task OpenDocument_PathsDifferingByCase_AreTwoTabsOnLinux()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/notes/Readme.adoc", "one");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        _fileService.OpenFileResult = ("/tmp/notes/README.adoc", "two");
        await vm.OpenDocumentCommand.ExecuteAsync(null);

        Assert.Equal(OperatingSystem.IsLinux() ? 2 : 1, vm.Documents.Count);
        Assert.Equal(OperatingSystem.IsLinux() ? "two" : "one", vm.SelectedDocument!.SourceText);
    }

    [Fact]
    public void SaveSession_WhileStartupIsOpeningTheTabs_KeepsTheSavedOne()
    {
        var vm = CreateViewModel();
        vm.OpeningDocuments = true; // the window closed before the session's files were read

        vm.SaveSession();

        Assert.Equal(0, _sessionStore.SaveCalled);
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
    public async Task CleanupRecoveryOnShutdown_RemovesOwnEntriesIncludingOrphans_KeepsOtherInstances()
    {
        var vm = CreateViewModel();
        // Found at startup but unloadable: must not prompt for recovery forever.
        _recoveryStore.SetEntries([new() { DocumentId = "orphan", DisplayName = "gone.adoc" }]);
        _dialogService.NextRecoveryResult = true;
        await vm.CheckAndRecoverAsync();
        // Written later by a second instance sharing the recovery folder.
        _recoveryStore.GetRecoveryEntries().Add(new() { DocumentId = "other", DisplayName = "theirs.adoc" });

        vm.CleanupRecoveryOnShutdown();

        Assert.Equal(["other"], _recoveryStore.GetRecoveryEntries().Select(e => e.DocumentId));
    }

    [Fact]
    public async Task CleanupRecoveryOnShutdown_OneFailedRemoval_StillRemovesTheRest()
    {
        var vm = CreateViewModel();
        _recoveryStore.SetEntries([new() { DocumentId = "locked", DisplayName = "a.adoc" }, new() { DocumentId = "b", DisplayName = "b.adoc" }]);
        _dialogService.NextRecoveryResult = true;
        await vm.CheckAndRecoverAsync();
        _recoveryStore.FailRemovalOf = "locked";

        vm.CleanupRecoveryOnShutdown();

        Assert.Equal(["locked"], _recoveryStore.GetRecoveryEntries().Select(e => e.DocumentId));
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

    // --- Save failures, encoding, concurrent edits ---

    private readonly List<string> _tempFiles = [];

    private string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ascolotl-vm-{Guid.NewGuid():N}.adoc");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task Save_WriteFails_ShowsSaveFailedAndKeepsDocumentDirty()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "modified";
        _fileService.SaveException = new UnauthorizedAccessException("Access denied");

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal("Save Failed", Assert.Single(_dialogService.Errors).Title);
        Assert.True(vm.SelectedDocument.IsDirty);
    }

    [Fact]
    public async Task AppClosing_WriteFails_AbortsClose()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "modified";
        _dialogService.NextResult = SaveConfirmationResult.Save;
        _fileService.SaveException = new IOException("Disk full");

        Assert.False(await vm.HandleAppClosingAsync());
        Assert.True(vm.SelectedDocument.IsDirty);
    }

    [Fact]
    public async Task Save_KeepsEncodingTheFileWasReadWith()
    {
        var vm = CreateViewModel();
        _fileService.ReadFileEncoding = System.Text.Encoding.UTF8; // with BOM
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.SelectedDocument!.SourceText = "modified";

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(3, _fileService.LastSavedEncoding!.GetPreamble().Length);
    }

    [Fact]
    public async Task Save_TypingDuringSave_StaysDirty()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/test.adoc", "original");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        var doc = vm.SelectedDocument!;
        doc.SourceText = "modified";
        _fileService.DuringSave = () => doc.SourceText += " and typed during save";

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal("modified", _fileService.LastSavedContent);
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public async Task Save_FileReplacedByAnOlderCopy_IsAConflict()
    {
        var vm = CreateViewModel();
        var path = CreateTempFile("on disk");
        _fileService.OpenFileResult = (path, "on disk");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        var doc = vm.SelectedDocument!;
        doc.SourceText = "my edits";
        File.SetLastWriteTimeUtc(path, doc.LastKnownWriteTime!.Value.AddHours(-1)); // a sync keeps the other machine's time

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ShowFileConflictCalled);
        Assert.Equal(0, _fileService.SaveFileCalled);
    }

    [Fact]
    public async Task Save_WritesEveryLineEndAsTheFileHadThem()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/crlf.adoc", "= Title\r\n\r\nText.\r\n");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        var doc = vm.SelectedDocument!;
        doc.SourceText += "\nNew paragraph.\n"; // as a block command adds lines

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal("= Title\r\n\r\nText.\r\n\r\nNew paragraph.\r\n", _fileService.LastSavedContent);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task Save_ThroughASymlink_SeesTheTargetChange()
    {
        var vm = CreateViewModel();
        var target = CreateTempFile("on disk");
        var link = target + ".link.adoc";
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // Windows without Developer Mode
        try
        {
            _fileService.OpenFileResult = (link, "on disk");
            await vm.OpenDocumentCommand.ExecuteAsync(null);
            var doc = vm.SelectedDocument!;
            doc.SourceText = "my edits";
            File.SetLastWriteTimeUtc(target, doc.LastKnownWriteTime!.Value.AddHours(1)); // another program wrote the file

            await vm.SaveDocumentCommand.ExecuteAsync(null);

            Assert.Equal(1, _dialogService.ShowFileConflictCalled);
            Assert.Equal(0, _fileService.SaveFileCalled);
        }
        finally
        {
            File.Delete(link);
        }
    }

    [Fact]
    public async Task SaveAll_ConflictAnsweredCancel_DoesNotSayAllSaved()
    {
        var vm = CreateViewModel();
        var path = CreateTempFile("on disk");
        _fileService.OpenFileResult = (path, "on disk");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        var conflicting = vm.SelectedDocument!;
        conflicting.SourceText = "my edits";
        conflicting.LastKnownWriteTime = File.GetLastWriteTimeUtc(path).AddMinutes(-1); // changed on disk since
        _dialogService.NextFileConflictResult = FileConflictResult.Cancel;

        await vm.SaveAllCommand.ExecuteAsync(null);

        Assert.True(conflicting.IsDirty);
        Assert.NotEqual(Ascolotl.Core.Resources.Strings.StatusAllSaved, vm.StatusText);
    }

    [Fact]
    public async Task SaveAll_ConflictAnsweredSaveAs_SavesTheConflictingDocument()
    {
        var vm = CreateViewModel();
        var path = CreateTempFile("on disk");
        _fileService.OpenFileResult = (path, "on disk");
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        var conflicting = vm.SelectedDocument!;
        conflicting.SourceText = "conflicting edits";
        conflicting.LastKnownWriteTime = File.GetLastWriteTimeUtc(path).AddMinutes(-1); // changed on disk since
        vm.NewDocumentCommand.Execute(null); // another tab is selected
        _dialogService.NextFileConflictResult = FileConflictResult.SaveAs;
        _fileService.SaveFileAsResult = "/tmp/copy.adoc";

        await vm.SaveAllCommand.ExecuteAsync(null);

        Assert.Equal("conflicting edits", _fileService.LastSavedContent);
        Assert.Equal("/tmp/copy.adoc", conflicting.FilePath);
        Assert.False(conflicting.IsDirty);
    }

    [Fact]
    public async Task CheckAndRecover_NoSnapshotLoads_ReturnsFalse()
    {
        var vm = CreateViewModel();
        _recoveryStore.SetEntries([new() { DocumentId = "lost", DisplayName = "lost.adoc" }]);
        _dialogService.NextRecoveryResult = true;

        Assert.False(await vm.CheckAndRecoverAsync());
    }

    [Fact]
    public async Task RestoreSession_AfterRecovery_AddsTheOtherTabsAndKeepsRecoveredInFront()
    {
        var vm = CreateViewModel();
        var recoveredPath = CreateTempFile("a");
        var otherPath = CreateTempFile("b");
        _recoveryStore.SetEntries([new() { DocumentId = "a", OriginalFilePath = recoveredPath, DisplayName = "a.adoc" }]);
        _recoveryStore.SetSnapshot("a", "a, edited");
        _dialogService.NextRecoveryResult = true;
        _sessionStore.NextLoadResult = new SessionState
        {
            SelectedIndex = 1,
            OpenDocuments =
            [
                new() { DocumentId = "a", FilePath = recoveredPath, DisplayName = "a.adoc" },
                new() { DocumentId = "b", FilePath = otherPath, DisplayName = "b.adoc" },
            ],
        };

        await vm.CheckAndRecoverAsync();
        await vm.RestoreSessionAsync();

        Assert.Equal(["a, edited", "b"], vm.Documents.Select(d => d.SourceText));
        Assert.Equal("a, edited", vm.SelectedDocument!.SourceText);
    }

    [Fact]
    public void F1_OnAMarkdownTab_ShowsTheMarkdownSyntax()
    {
        var vm = CreateViewModel();
        _fileService.OpenFileResult = ("/tmp/notes.md", "# Notes");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.ShowSyntaxReferenceCommand.Execute(null);

        Assert.True(vm.SyntaxReference.Markdown);
    }

    [Fact]
    public void SaveSession_FrontTabIndexCountsSavedTabsOnly_AndKeepsTopLines()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null); // untitled: not saved
        _fileService.OpenFileResult = ("/tmp/a.adoc", "a");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);
        _fileService.OpenFileResult = ("/tmp/b.adoc", "b");
        _ = vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.Documents[2].TopLine = 42;
        vm.Documents[2].PageScroll = 0.3;
        vm.SelectedDocument = vm.Documents[2];
        vm.SetEditorModeCommand.Execute(EditorMode.Split);

        vm.SaveSession();

        Assert.Equal(1, _sessionStore.LastSavedState!.SelectedIndex);
        Assert.Equal([0, 42], _sessionStore.LastSavedState.OpenDocuments.Select(d => d.TopLine));
        Assert.Equal([EditorMode.Wysiwyg, EditorMode.Split], _sessionStore.LastSavedState.OpenDocuments.Select(d => d.EditorMode));
        Assert.Equal([0, 0.3], _sessionStore.LastSavedState.OpenDocuments.Select(d => d.PageScroll));
    }

    [Fact]
    public async Task RestoreSession_FrontTabByPath_WhenAnEarlierFileIsGone()
    {
        var vm = CreateViewModel();
        var a = CreateTempFile("a");
        var b = CreateTempFile("b");
        _sessionStore.NextLoadResult = new SessionState
        {
            SelectedIndex = 2,
            OpenDocuments =
            [
                new() { DocumentId = "gone", FilePath = Path.Combine(Path.GetTempPath(), $"gone-{Guid.NewGuid():N}.adoc"), DisplayName = "gone.adoc" },
                new() { DocumentId = "a", FilePath = a, DisplayName = "a.adoc", EditorMode = (EditorMode)42 },
                new() { DocumentId = "b", FilePath = b, DisplayName = "b.adoc", TopLine = 7, EditorMode = EditorMode.Source },
            ],
        };

        await vm.RestoreSessionAsync();

        Assert.Equal("b", vm.SelectedDocument!.SourceText);
        Assert.Equal(7, vm.SelectedDocument.TopLine);
        Assert.Equal(EditorMode.Source, vm.EditorMode);
        Assert.Equal(EditorMode.Wysiwyg, vm.Documents[0].EditorMode); // an unknown mode in the file
    }

    [Fact]
    public async Task CheckAndRecover_UsesWriteTimeStoredWithSnapshot()
    {
        var vm = CreateViewModel();
        var path = CreateTempFile("changed while the app was down");
        var basedOn = File.GetLastWriteTimeUtc(path).AddMinutes(-5);
        _recoveryStore.SetEntries([new() { DocumentId = "d", OriginalFilePath = path, DisplayName = "d.adoc", FileWriteTime = basedOn }]);
        _recoveryStore.SetSnapshot("d", "recovered");
        _dialogService.NextRecoveryResult = true;

        await vm.CheckAndRecoverAsync();
        await vm.SaveDocumentCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogService.ShowFileConflictCalled);
        Assert.Equal(0, _fileService.SaveFileCalled); // conflict dialog cancelled
    }

    [Fact]
    public void Autosave_PassesBaseWriteTimeToRecoveryStore()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        var doc = vm.SelectedDocument!;
        doc.SourceText = "dirty";
        doc.LastKnownWriteTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        vm.RunAutosave();

        Assert.Equal(doc.LastKnownWriteTime, _recoveryStore.LastSavedFileWriteTime);
    }
}
