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
        TypeInfoResolver = StoreJson.Default,
    };

    private readonly string _recoveryDirectory;
    private readonly string _manifestPath;

    // Autosave, crash save and save/close all touch the manifest (read-modify-write).
    private readonly Lock _lock = new();

    public RecoveryStore()
        : this(GetDefaultDirectory())
    {
    }

    public RecoveryStore(string recoveryDirectory)
    {
        _recoveryDirectory = recoveryDirectory;
        _manifestPath = Path.Combine(_recoveryDirectory, "recovery-manifest.json");
    }

    public void SaveSnapshot(string documentId, string sourceText, string? filePath, string displayName, DateTime? fileWriteTime = null)
    {
        lock (_lock)
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
                FileWriteTime = fileWriteTime,
            };

            if (existing >= 0)
                entries[existing] = entry;
            else
                entries.Add(entry);

            SaveManifest(entries);
        }
    }

    public string? LoadSnapshot(string documentId)
    {
        lock (_lock)
        {
            var path = GetSnapshotPath(documentId);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    public void RemoveSnapshot(string documentId)
    {
        lock (_lock)
        {
            var path = GetSnapshotPath(documentId);
            if (File.Exists(path))
                File.Delete(path);

            var entries = LoadManifest();
            entries.RemoveAll(e => e.DocumentId == documentId);
            SaveManifest(entries);
        }
    }

    public List<RecoveryEntry> GetRecoveryEntries()
    {
        lock (_lock)
            return LoadManifest();
    }

    public void ClearAll()
    {
        lock (_lock)
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
    }

    private string GetSnapshotPath(string documentId) =>
        Path.Combine(_recoveryDirectory, $"{documentId}.adoc");

    private List<RecoveryEntry> LoadManifest()
    {
        try
        {
            if (!File.Exists(_manifestPath))
                return SnapshotsOnDisk();

            var json = File.ReadAllText(_manifestPath);
            return JsonSerializer.Deserialize<List<RecoveryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            // The next save overwrites the manifest: keep the unreadable one for inspection.
            try { File.Copy(_manifestPath, _manifestPath + ".corrupt", overwrite: true); }
            catch { /* best effort */ }
            return SnapshotsOnDisk();
        }
    }

    /// <summary>Without a readable manifest, the snapshots still on disk: offered back as unsaved
    /// documents, their file and name lost, named after their title.</summary>
    private List<RecoveryEntry> SnapshotsOnDisk()
    {
        try
        {
            if (!Directory.Exists(_recoveryDirectory)) return [];
            var n = 0;
            return [.. Directory.EnumerateFiles(_recoveryDirectory)
                .Where(f => Path.GetExtension(f).Equals(".adoc", StringComparison.OrdinalIgnoreCase))
                .Select(f => new RecoveryEntry
                {
                    DocumentId = Path.GetFileNameWithoutExtension(f),
                    DisplayName = Title(f) ?? string.Format(Ascolotl.Core.Resources.Strings.Untitled, ++n),
                    Timestamp = File.GetLastWriteTimeUtc(f),
                })];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Title(string snapshot) =>
        File.ReadLines(snapshot).Take(20).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("= ") || l.StartsWith("# ")) is { } heading
            ? heading[2..].Trim() : null;

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
            // Create: on a fresh Mac or Linux, ~/.config may not exist yet, and an empty path would be relative.
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
            "Ascolotl",
            "recovery");
}
