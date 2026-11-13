using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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
            _log.Info("Ascolotl starting");

            // Hook unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _settingsService = new JsonSettingsService();
            _settings = _settingsService.Load();

            ApplyTheme(_settings.Theme);

            var mainWindow = new Views.MainWindow();

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
                docxImportService: new DocxImportService())
            {
                CurrentTheme = _settings.Theme,
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
            };

            _viewModel.LoadRecentFiles(_settings.RecentFiles);

            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.CurrentTheme))
                {
                    ApplyTheme(_viewModel.CurrentTheme);
                    _settings.Theme = _viewModel.CurrentTheme;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.EditorMode))
                {
                    _settings.EditorMode = _viewModel.EditorMode;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.ShowTitleAndToc))
                {
                    _settings.ShowTitleAndToc = _viewModel.ShowTitleAndToc;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.SyncScrolling))
                {
                    _settings.SyncScrolling = _viewModel.SyncScrolling;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.ShowOutlinePane))
                {
                    _settings.ShowOutlinePane = _viewModel.ShowOutlinePane;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.HamburgerMenu))
                {
                    _settings.HamburgerMenu = _viewModel.HamburgerMenu;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.ProjectFolderPath))
                {
                    _settings.ProjectFolderPath = _viewModel.ProjectFolderPath;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.PagePreview))
                {
                    _settings.PagePreview = _viewModel.PagePreview;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.StylePath))
                {
                    _settings.StylePath = _viewModel.StylePath;
                    _settingsService.Save(_settings);
                }
                else if (e.PropertyName is nameof(MainWindowViewModel.PdfTitleRule) or nameof(MainWindowViewModel.AskPdfTitle))
                {
                    _settings.PdfTitleRule = _viewModel.PdfTitleRule;
                    _settings.AskPdfTitle = _viewModel.AskPdfTitle;
                    _settingsService.Save(_settings);
                }
            };

            _viewModel.RecentFilesChanged += () =>
            {
                _settings.RecentFiles = [.. _viewModel.RecentFiles];
                _settingsService.Save(_settings);
            };

            _viewModel.LoadExportProfiles(_settings.ExportProfiles);
            _viewModel.ExportProfilesChanged += () =>
            {
                _settings.ExportProfiles = [.. _viewModel.ExportProfiles];
                _settingsService.Save(_settings);
            };

            // Reopen the project workspace from the previous run (after the
            // PropertyChanged hook so a vanished folder clears the setting).
            if (_settings.ProjectFolderPath is { Length: > 0 } projectFolder)
                _viewModel.LoadProjectFolder(projectFolder);

            mainWindow.DataContext = _viewModel;

            RestoreWindowBounds(mainWindow, _settings);

            mainWindow.Opened += async (_, _) =>
            {
                // Recovery takes precedence over session restore
                var recovered = await _viewModel.CheckAndRecoverAsync();
                if (!recovered)
                    await _viewModel.RestoreSessionAsync();

                _viewModel.StartAutosave();
            };

            var forceClose = false;
            mainWindow.Closing += async (_, e) =>
            {
                if (forceClose)
                {
                    SaveWindowBounds(mainWindow);
                    _viewModel.StopAutosave();
                    _viewModel.SaveSession();
                    _viewModel.CleanupRecoveryOnShutdown();
                    _log?.Info("Ascolotl shutdown complete");
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

    private void SaveWindowBounds(Avalonia.Controls.Window window)
    {
        if (_settings is null || _settingsService is null)
            return;

        _settings.WindowMaximized = window.WindowState == Avalonia.Controls.WindowState.Maximized;

        if (!_settings.WindowMaximized)
        {
            _settings.WindowWidth = window.Width;
            _settings.WindowHeight = window.Height;
            _settings.WindowX = window.Position.X;
            _settings.WindowY = window.Position.Y;
        }

        _settingsService.Save(_settings);
    }
}
