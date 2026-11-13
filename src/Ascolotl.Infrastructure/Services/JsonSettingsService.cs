using System.Text.Json;
using System.Text.Json.Serialization;
using Ascolotl.Core.Models;

namespace Ascolotl.Infrastructure.Services;

public sealed class JsonSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _settingsPath;

    public JsonSettingsService()
        : this(GetDefaultPath())
    {
    }

    public JsonSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = _settingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    private static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ascolotl",
            "settings.json");
}
