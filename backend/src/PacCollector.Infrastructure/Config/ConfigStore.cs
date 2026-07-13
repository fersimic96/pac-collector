using System.Text.Json;

namespace PacCollector.Infrastructure.Config;

// carga settings.json al arrancar, atomic write con tmp + rename + fsync
public sealed class ConfigStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private AppConfig _config;

    public event EventHandler<AppConfig>? Changed;

    private ConfigStore(string path, AppConfig config)
    {
        _path = path;
        _config = config;
    }

    public static ConfigStore Load(string path)
    {
        if (!File.Exists(path))
            return new ConfigStore(path, new AppConfig());

        string raw;
        try { raw = File.ReadAllText(path); }
        catch { return new ConfigStore(path, new AppConfig()); }

        try
        {
            var cfg = ParseWithMigration(raw);
            return new ConfigStore(path, cfg);
        }
        catch
        {
            TryBackupCorrupt(path);
            return new ConfigStore(path, new AppConfig());
        }
    }

    public AppConfig Snapshot()
    {
        lock (_lock) return _config.Clone();
    }

    public void Replace(AppConfig newConfig)
    {
        var parent = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        // clone defensivo del input asi el caller no puede mutarlo despues de Replace
        var owned = newConfig.Clone();
        var json = JsonSerializer.Serialize(owned, JsonOptions.Pretty);
        var tmp = $"{_path}.{Guid.NewGuid():N}.tmp";

        try
        {
            WriteAtomically(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }

        lock (_lock) _config = owned;
        Changed?.Invoke(this, owned.Clone());
    }

    // serializa el json al tmp con flush a disco antes del rename
    private static void WriteAtomically(string tmp, string content)
    {
        using var fs = new FileStream(
            tmp,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        using var sw = new StreamWriter(fs);
        sw.Write(content);
        sw.Flush();
        fs.Flush(flushToDisk: true);
    }

    private static AppConfig ParseWithMigration(string raw)
    {
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        uint version = AppConfig.CurrentVersion;
        if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number)
            version = v.GetUInt32();

        if (version > AppConfig.CurrentVersion)
            throw new InvalidDataException(
                $"settings.json has version {version}, this build supports up to {AppConfig.CurrentVersion}. Update the app first.");

        return JsonSerializer.Deserialize<AppConfig>(raw, JsonOptions.Default)
               ?? new AppConfig();
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            var ts = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var name = Path.GetFileName(path);
            var backup = Path.Combine(Path.GetDirectoryName(path) ?? "", $"{name}.broken-{ts}");
            File.Move(path, backup, overwrite: false);
        }
        catch { /* logged elsewhere */ }
    }
}
