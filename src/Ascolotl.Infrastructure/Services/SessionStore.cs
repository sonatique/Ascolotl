using System.Text.Json;
using System.Text.Json.Serialization;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Infrastructure.Services;

public sealed class SessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _sessionPath;

    public SessionStore()
        : this(GetDefaultPath())
    {
    }

    public SessionStore(string sessionPath)
    {
        _sessionPath = sessionPath;
    }

    public void Save(SessionState state)
    {
        var directory = Path.GetDirectoryName(_sessionPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = _sessionPath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _sessionPath, overwrite: true);
    }

    public SessionState? Load()
    {
        try
        {
            if (!File.Exists(_sessionPath))
                return null;

            var json = File.ReadAllText(_sessionPath);
            return JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ascolotl",
            "session.json");
}
