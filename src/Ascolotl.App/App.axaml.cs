using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Ascolotl.App.Services;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;
using Ascolotl.Core.ViewModels;
using Ascolotl.Export;
using Ascolotl.Infrastructure.Services;

namespace Ascolotl.App;

public class App : Application
{
    private JsonSettingsService? _settingsService;
    private AppSettings? _settings;
    private AdocRenderService? _renderService;
    private FileAppLog? _log;
    private MainWindowViewModel? _viewModel;

    /// <summary>Set by Program when a running instance held the lock but never accepted the files.</summary>
    internal static bool ForwardFailed { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize logging first
            _log = new FileAppLog();
            _log.Info($"Ascolotl starting ({SinceStart()} ms after launch)");
            if (ForwardFailed)
                _log.Warn("Another instance is running but did not answer for 10 s; starting anyway");

            // Hook unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _settingsService = new JsonSettingsService(_log);
            _settings = _settingsService.Load();

            // Before any window is built: views read their texts once, when created.
            UiLanguage.Apply(_settings.Language);
            ApplyTheme(_settings.Theme);

            var mainWindow = new Views.MainWindow();

            // Listen for later launches right away, so one started during the
            // (possibly long) startup below forwards its files instead of
            // running a second instance. Paths received before startup ends
            // wait in pendingPaths.
            List<string>? pendingPaths = [];
            _ = SingleInstance.ListenAsync(paths => Dispatcher.UIThread.Post(async () =>
            {
                if (pendingPaths is not null)
                {
                    pendingPaths.AddRange(paths);
                    return;
                }
                await OpenFilesAsync(paths);
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }));

            var fileService = new AvaloniaFileService(mainWindow);
            var dialogService = new AvaloniaDialogService(mainWindow);

            _renderService = new AdocRenderService();
            var exportService = new ExportService();
            var recoveryStore = new RecoveryStore();
            var sessionStore = new SessionStore();

            _viewModel = new MainWindowViewModel(
                fileService, dialogService, _renderService, exportService,
                recoveryStore, sessionStore, _log,
                websiteExportService: new WebsiteExportService(),
                importService: new DocumentImportService())
            {
                CurrentTheme = _settings.Theme,
                UiLanguageCode = _settings.Language,
                EditorMode = _settings.EditorMode,
                ShowTitleAndToc = _settings.ShowTitleAndToc,
                SyncScrolling = _settings.SyncScrolling,
                ShowOutlinePane = _settings.ShowOutlinePane,
                HamburgerMenu = _settings.HamburgerMenu,
                PagePreview = _settings.PagePreview,
                // A theme file deleted since last run falls back to the built-in look.
                StylePath = File.Exists(_settings.StylePath) ? _settings.StylePath : null,
                PdfTitleRule = _settings.PdfTitleRule,
                AskPdfTitle = _settings.AskPdfTitle,
                AccessiblePdf = _settings.AccessiblePdf,
            };

            _viewModel.LoadRecentFiles(_settings.RecentFiles);

            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.CurrentTheme))
                {
                    ApplyTheme(_viewModel.CurrentTheme);
                    _settings.Theme = _viewModel.CurrentTheme;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.UiLanguageCode))
                {
                    _settings.Language = _viewModel.UiLanguageCode;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.EditorMode))
                {
                    _settings.EditorMode = _viewModel.EditorMode;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.ShowTitleAndToc))
                {
                    _settings.ShowTitleAndToc = _viewModel.ShowTitleAndToc;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.SyncScrolling))
                {
                    _settings.SyncScrolling = _viewModel.SyncScrolling;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.ShowOutlinePane))
                {
                    _settings.ShowOutlinePane = _viewModel.ShowOutlinePane;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.HamburgerMenu))
                {
                    _settings.HamburgerMenu = _viewModel.HamburgerMenu;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.ProjectFolderPath))
                {
                    _settings.ProjectFolderPath = _viewModel.ProjectFolderPath;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.PagePreview))
                {
                    _settings.PagePreview = _viewModel.PagePreview;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.StylePath))
                {
                    _settings.StylePath = _viewModel.StylePath;
                    SaveSettings();
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.AccessiblePdf))
                {
                    _settings.AccessiblePdf = _viewModel.AccessiblePdf;
                    SaveSettings();
                }
                else if (e.PropertyName is nameof(MainWindowViewModel.PdfTitleRule) or nameof(MainWindowViewModel.AskPdfTitle))
                {
                    _settings.PdfTitleRule = _viewModel.PdfTitleRule;
                    _settings.AskPdfTitle = _viewModel.AskPdfTitle;
                    SaveSettings();
                }
            };

            _viewModel.RecentFilesChanged += () =>
            {
                _settings.RecentFiles = [.. _viewModel.RecentFiles];
                SaveSettings();
            };

            _viewModel.LoadExportProfiles(_settings.ExportProfiles);
            _viewModel.ExportProfilesChanged += () =>
            {
                _settings.ExportProfiles = [.. _viewModel.ExportProfiles];
                SaveSettings();
            };

            // Reopen the project workspace from the previous run (after the
            // PropertyChanged hook so a vanished folder clears the setting).
            if (_settings.ProjectFolderPath is { Length: > 0 } projectFolder)
                _viewModel.LoadProjectFolder(projectFolder);

            mainWindow.DataContext = _viewModel;
            _viewModel.OpeningDocuments = true;

            RestoreWindowBounds(mainWindow, _settings);
            mainWindow.SidebarWidth = _settings.SidebarWidth;

            mainWindow.Opened += async (_, _) =>
            {
                // Recovered documents first; the session then adds the other
                // tabs (it skips files already open).
                var windowShown = SinceStart();
                long sessionRestored;
                try
                {
                    await _viewModel.CheckAndRecoverAsync();
                    await _viewModel.RestoreSessionAsync();
                    sessionRestored = SinceStart();

                    // Files Ascolotl was launched with (e.g. double-clicked .adoc files)
                    // open after the restored tabs, so the last one is in front;
                    // then those from launches made during startup.
                    await OpenFilesAsync(desktop.Args ?? []);
                    var queued = pendingPaths;
                    pendingPaths = null;
                    await OpenFilesAsync(queued ?? []);
                }
                finally
                {
                    pendingPaths = null;
                    _viewModel.OpeningDocuments = false;
                }

                _viewModel.StartAutosave();

                // Startup timing, for spotting regressions: logged once the first
                // preview has had its layout pass (background priority runs after it).
                await _viewModel.PendingRenderTask;
                Dispatcher.UIThread.Post(
                    () => _log?.Info($"Startup: window {windowShown} ms, session {sessionRestored} ms, " +
                                     $"first preview {SinceStart()} ms after launch"),
                    DispatcherPriority.Background);
            };

            var forceClose = false;
            mainWindow.Closing += async (_, e) =>
            {
                if (forceClose)
                {
                    try
                    {
                        // Recovery cleanup first: a failure saving bounds or session must not leave stale entries.
                        _viewModel.StopAutosave();
                        _viewModel.CleanupRecoveryOnShutdown();
                        SaveWindowBounds(mainWindow);
                        _viewModel.SaveSession();
                        _log?.Info("Ascolotl shutdown complete");
                    }
                    catch (Exception ex)
                    {
                        _log?.Error("Shutdown cleanup failed", ex);
                    }
                    _log?.Dispose();
                    return;
                }

                e.Cancel = true;

                var canClose = await _viewModel.HandleAppClosingAsync();
                if (canClose)
                {
                    forceClose = true;
                    mainWindow.Close();
                }
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _log?.Error("Unhandled exception", ex);

        // Emergency save before crashing
        _viewModel?.EmergencyRecoverySave();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error("Unobserved task exception", e.Exception);
        e.SetObserved(); // Prevent app crash from background task failures
    }

    private void ApplyTheme(AppTheme theme)
    {
        RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    private static long SinceStart()
        => (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    /// <summary>Opens each existing file among <paramref name="paths"/>; other arguments are ignored.</summary>
    private async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
            await _viewModel!.OpenFileByPathAsync(Path.GetFullPath(path));
    }

    /// <summary>Settings are saved from event handlers: a failed write is logged, never a crash.</summary>
    private void SaveSettings()
    {
        try
        {
            _settingsService?.Save(_settings!);
        }
        catch (Exception ex)
        {
            _log?.Error("Could not save settings", ex);
        }
    }

    private static void RestoreWindowBounds(Avalonia.Controls.Window window, AppSettings settings)
    {
        window.Width = settings.WindowWidth;
        window.Height = settings.WindowHeight;

        if (settings.WindowMaximized)
        {
            window.WindowState = Avalonia.Controls.WindowState.Maximized;
        }
        else
        {
            window.Position = new Avalonia.PixelPoint(
                (int)settings.WindowX, (int)settings.WindowY);
        }
    }

    private void SaveWindowBounds(Views.MainWindow window)
    {
        if (_settings is null || _settingsService is null)
            return;

        _settings.SidebarWidth = window.SidebarWidth;
        _settings.WindowMaximized = window.WindowState == Avalonia.Controls.WindowState.Maximized;

        if (!_settings.WindowMaximized)
        {
            _settings.WindowWidth = window.Width;
            _settings.WindowHeight = window.Height;
            _settings.WindowX = window.Position.X;
            _settings.WindowY = window.Position.Y;
        }

        SaveSettings();
    }
}
