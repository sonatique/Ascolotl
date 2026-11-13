using Ascolotl.Core.Services;

namespace Ascolotl.Infrastructure.Services;

public sealed class FileAppLog : IAppLog, IDisposable
{
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string? _currentDate;

    public FileAppLog()
        : this(GetDefaultLogDirectory())
    {
    }

    public FileAppLog(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        Task.Run(CleanOldLogs); // Defer cleanup off UI thread
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is not null
            ? $"{message} — {exception.GetType().Name}: {exception.Message}\n{exception}"
            : message;
        Write("ERROR", text);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            try
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                if (_currentDate != today)
                {
                    _writer?.Dispose();
                    _currentDate = today;
                    var path = Path.Combine(_logDirectory, $"adocforge-{today}.log");
                    var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    _writer = new StreamWriter(stream) { AutoFlush = true };
                }

                _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}");
            }
            catch
            {
                // Logging must never crash the app
            }
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(_logDirectory, "adocforge-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static string GetDefaultLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ascolotl",
            "logs");
}
