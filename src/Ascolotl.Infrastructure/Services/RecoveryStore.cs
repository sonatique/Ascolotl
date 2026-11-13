using System.Text.Json;
using System.Text.Json.Serialization;
using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Infrastructure.Services;

public sealed class RecoveryStore : IRecoveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _recoveryDirectory;
    private readonly string _manifestPath;

    public RecoveryStore()
        : this(GetDefaultDirectory())
    {
    }

    public RecoveryStore(string recoveryDirectory)
    {
        _recoveryDirectory = recoveryDirectory;
        _manifestPath = Path.Combine(_recoveryDirectory, "recovery-manifest.json");
    }

    public void SaveSnapshot(string documentId, string sourceText, string? filePath, string displayName)
    {
        Directory.CreateDirectory(_recoveryDirectory);

        // Write source text atomically
        var snapshotPath = GetSnapshotPath(documentId);
        var tempPath = snapshotPath + ".tmp";
        File.WriteAllText(tempPath, sourceText);
        File.Move(tempPath, snapshotPath, overwrite: true);

        // Update manifest
        var entries = LoadManifest();
        var existing = entries.FindIndex(e => e.DocumentId == documentId);
        var entry = new RecoveryEntry
        {
            DocumentId = documentId,
            OriginalFilePath = filePath,
            DisplayName = displayName,
            Timestamp = DateTime.UtcNow,
        };

        if (existing >= 0)
            entries[existing] = entry;
        else
            entries.Add(entry);

        SaveManifest(entries);
    }

    public string? LoadSnapshot(string documentId)
    {
        var path = GetSnapshotPath(documentId);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void RemoveSnapshot(string documentId)
    {
        var path = GetSnapshotPath(documentId);
        if (File.Exists(path))
            File.Delete(path);

        var entries = LoadManifest();
        entries.RemoveAll(e => e.DocumentId == documentId);
        SaveManifest(entries);
    }

    public List<RecoveryEntry> GetRecoveryEntries() => LoadManifest();

    public void ClearAll()
    {
        try
        {
            if (Directory.Exists(_recoveryDirectory))
                Directory.Delete(_recoveryDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private string GetSnapshotPath(string documentId) =>
        Path.Combine(_recoveryDirectory, $"{documentId}.adoc");

    private List<RecoveryEntry> LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath))
                return [];

            var json = File.ReadAllText(_manifestPath);
            return JsonSerializer.Deserialize<List<RecoveryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveManifest(List<RecoveryEntry> entries)
    {
        Directory.CreateDirectory(_recoveryDirectory);
        var tempPath = _manifestPath + ".tmp";
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _manifestPath, overwrite: true);
    }

    private static string GetDefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Ascolotl",
            "recovery");
}
