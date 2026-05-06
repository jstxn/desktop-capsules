using DesktopCapsules.Models;
using System.Text.Json;

namespace DesktopCapsules.Services;

public sealed class CapsuleStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _saveLock = new();

    public CapsuleStore()
    {
        RootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DesktopCapsules");

        GroupsPath = Path.Combine(RootPath, "Groups");
        RemovedPath = Path.Combine(RootPath, "Removed");
        ConfigPath = Path.Combine(RootPath, "layout.json");

        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(GroupsPath);
        Directory.CreateDirectory(RemovedPath);
    }

    public string RootPath { get; }

    public string GroupsPath { get; }

    public string RemovedPath { get; }

    public string ConfigPath { get; }

    public CapsuleConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new CapsuleConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<CapsuleConfig>(json, SerializerOptions) ?? new CapsuleConfig();
        }
        catch
        {
            var backupPath = Path.Combine(RootPath, $"layout.bad-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try
            {
                File.Copy(ConfigPath, backupPath, overwrite: true);
            }
            catch
            {
                // Loading should still recover even if the corrupt-file backup cannot be written.
            }

            return new CapsuleConfig();
        }
    }

    public void Save(CapsuleConfig config)
    {
        lock (_saveLock)
        {
            Directory.CreateDirectory(RootPath);
            var tempPath = Path.Combine(RootPath, $"layout.{Guid.NewGuid():N}.tmp");
            var backupPath = ConfigPath + ".bak";

            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, config, SerializerOptions);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(ConfigPath))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Replace(tempPath, ConfigPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, ConfigPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    public string GetGroupPath(string capsuleId)
    {
        var path = Path.Combine(GroupsPath, capsuleId);
        Directory.CreateDirectory(path);
        return path;
    }
}
