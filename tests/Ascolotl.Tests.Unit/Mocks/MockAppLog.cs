using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockAppLog : IAppLog
{
    public List<string> Messages { get; } = [];

    public void Info(string message) => Messages.Add($"[INFO] {message}");
    public void Warn(string message) => Messages.Add($"[WARN] {message}");
    public void Error(string message, Exception? exception = null) =>
        Messages.Add($"[ERROR] {message}" + (exception is not null ? $" — {exception.Message}" : ""));
}
