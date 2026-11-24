using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Runtime.InteropServices;
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
    private readonly List<PosixSignalRegistration> _signals = []; // kept alive: a collected registration stops listening

    /// <summary>Set by Program when a running instance held the lock but never accepted the files.</summary>
    internal static bool ForwardFailed { get; set; }

    static App()
    {
        // A window drawing its own title bar moves by it: Avalonia leaves that to the app. Every dialog and
        // tool window; the main window's title row, with its tabs, does it itself.
        InputElement.PointerPressedEvent.AddClassHandler<Window>((window, e) =>
        {
            if (window is Views.MainWindow || !window.ExtendClientAreaToDecorationsHint) return;
            var press = e.GetCurrentPoint(window);
            if (press.Properties.IsLeftButtonPressed && press.Position.Y < window.ExtendClientAreaTitleBarHeightHint)
                window.BeginMoveDrag(e);
        });
    }

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

            // Spelling: the user's dictionaries and words beside the settings, then the app's own dictionaries.
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Ascolotl");
            Core.Spelling.Speller.Folders = [Path.Combine(data, "dictionaries"), Path.Combine(AppContext.BaseDirectory, "dictionaries")];
            Core.Spelling.Speller.Personal.Load(Path.Combine(data, "words.txt"));
            Core.Spelling.SpellCheck.Enabled = _settings.CheckSpelling;

            Export.Formulas.WarmUp();

            // Before any window is built: views read their texts once, when created.
            UiLanguage.Apply(_settings.Language);
            ApplyTheme(_settings.Theme);

            var mainWindow = new Views.MainWindow();

            // Listen for later launches right away, so one started during the
            // (possibly long) startup below forwards its files instead of
            // running a second instance. Paths received before startup ends
            // wait in pendingPaths.
            List<string>? pendingPaths = [];
            void Receive(IReadOnlyList<string> paths) => Dispatcher.UIThread.Post(async () =>
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
            });
            _ = SingleInstance.ListenAsync(Receive);
            // macOS hands files opened from Finder to the app as an event, not as arguments.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs opened)
                        Receive(opened.Files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
                };

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
                ShowTitleAndToc = _settings.ShowTitleAndToc,
                SyncScrolling = _settings.SyncScrolling,
                CheckSpelling = _settings.CheckSpelling,
                ShowOutlinePane = _settings.ShowOutlinePane,
                HamburgerMenu = _settings.HamburgerMenu,
                PagePreview = _settings.PagePreview,
                // A theme file deleted since last run falls back to the built-in look.
                StylePath = File.Exists(_settings.StylePath) ? _settings.StylePath : null,
                PdfTitleRule = _settings.PdfTitleRule,
                AskPdfTitle = _settings.AskPdfTitle,
                AccessiblePdf = _settings.AccessiblePdf,
                KrokiServer = _settings.KrokiServer,
                DiagramMode = _settings.Diagrams,
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
                else if (e.PropertyName == nameof(MainWindowViewModel.CheckSpelling))
                {
                    _settings.CheckSpelling = _viewModel.CheckSpelling;
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
                else if (e.PropertyName == nameof(MainWindowViewModel.DiagramMode))
                {
                    _settings.Diagrams = _viewModel.DiagramMode;
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
                // A system shutdown may not wait for the answer below: snapshot unsaved work first.
                _viewModel.RunAutosave();

                var canClose = await _viewModel.HandleAppClosingAsync();
                if (canClose)
                {
                    forceClose = true;
                    mainWindow.Close();
                }
            };

            // Logging out, shutting down or `kill` ends the app on Linux and macOS without
            // closing its window: snapshot unsaved work before the process goes.
            if (!OperatingSystem.IsWindows())
                foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP })
                    _signals.Add(PosixSignalRegistration.Create(signal, _ =>
                    {
                        try { Dispatcher.UIThread.Invoke(_viewModel.RunAutosave, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2)); }
                        catch (Exception) { /* the process is ending either way */ }
                    }));

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

    /// <summary>Opens each existing file among <paramref name="paths"/>, from Explorer or the command line,
    /// in Read view: opened to be read, until the user chooses to edit. Other arguments are ignored.</summary>
    private async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
            await _viewModel!.OpenFileByPathAsync(Path.GetFullPath(path), EditorMode.Read);
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
        var saved = new Avalonia.PixelPoint((int)settings.WindowX, (int)settings.WindowY);
        var screens = window.Screens.All.Select(s => (s.WorkingArea, s.Scaling)).ToList();
        if (FitToScreen(saved, settings.WindowWidth, settings.WindowHeight, screens) is { } fit)
        {
            window.Position = fit.Position; // before maximizing, so it maximizes on that screen
            window.Width = fit.Width;
            window.Height = fit.Height;
        }
        else
        {
            // Its screen is gone (undocked, or a minimized window's -32000).
            var primary = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
            window.Width = primary is null ? settings.WindowWidth : Math.Min(settings.WindowWidth, primary.WorkingArea.Width / primary.Scaling);
            window.Height = primary is null ? settings.WindowHeight : Math.Min(settings.WindowHeight, primary.WorkingArea.Height / primary.Scaling);
            window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
        }

        if (settings.WindowMaximized)
            window.WindowState = Avalonia.Controls.WindowState.Maximized;
    }

    /// <summary>
    /// The saved bounds moved and shrunk to fit the screen that holds the title bar's
    /// left end, or null when no screen does. Position and screen areas are in pixels,
    /// the size in DIPs.
    /// </summary>
    internal static (Avalonia.PixelPoint Position, double Width, double Height)? FitToScreen(
        Avalonia.PixelPoint position, double width, double height, IEnumerable<(Avalonia.PixelRect Area, double Scaling)> screens)
    {
        var grip = new Avalonia.PixelPoint(position.X + 40, position.Y + 10);
        foreach (var (area, scaling) in screens)
        {
            if (!area.Contains(grip)) continue;
            var w = Math.Min(width, area.Width / scaling);
            var h = Math.Min(height, area.Height / scaling);
            var x = Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(w * scaling)));
            var y = Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(h * scaling)));
            return (new Avalonia.PixelPoint(x, y), w, h);
        }
        return null;
    }

    private void SaveWindowBounds(Views.MainWindow window)
    {
        if (_settings is null || _settingsService is null)
            return;

        _settings.SidebarWidth = window.SidebarWidth;
        // A minimized window sits off-screen (-32000 on Windows): keep the bounds from before.
        if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            SaveSettings();
            return;
        }
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
