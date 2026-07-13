using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PacCollector.Infrastructure.Plugins;
using PacCollector.Infrastructure.Plugins.Builtin;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Api.Services;

public enum PluginKind { Lims, Print }

// resultado de la subida o borrado de un plugin. Si Ok=false, Errors trae la lista
// de problemas detectados. Si hubo backup de un spec previo, BackupPath lo indica
// (para que el usuario pueda restaurarlo manualmente si algo se rompio).
public sealed record PluginUploadResult(
    bool Ok,
    IReadOnlyList<string> Errors,
    string? SavedPath,
    string? BackupPath,
    int ActivePluginsCount);

// service que centraliza upload/delete/reload de specs. Diseno:
//   - valida el JSON ANTES de tocar el disco
//   - si va a sobreescribir un spec existente, hace backup .bak-<ts> primero
//   - despues del save, intenta Reload del registry
//   - si el reload tira excepcion, revierte: borra el .json nuevo y restaura el backup
//   - los .json embebidos en el assembly NO son borrables (solo los del override dir)
public sealed class PluginUploadService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    private static readonly JsonSerializerOptions PersistOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly PluginRegistryImpl _registry;
    private readonly string _limsDir;
    private readonly string _printDir;
    private readonly Lock _gate = new();

    public PluginUploadService(PluginRegistryImpl registry, string limsDir, string printDir)
    {
        _registry = registry;
        _limsDir = limsDir;
        _printDir = printDir;
    }

    public PluginUploadResult Upload(PluginKind kind, string rawJson)
    {
        lock (_gate)
        {
            // 1) parse + validate sin tocar disco
            var validation = ValidateRawJson(kind, rawJson);
            if (!validation.Ok)
                return new PluginUploadResult(false, validation.Errors, null, null, ActivePluginsCount());

            var pluginId = validation.PluginId;
            var normalizedJson = validation.NormalizedJson;
            var targetDir = DirFor(kind);
            var path = Path.Combine(targetDir, SafeFilename(pluginId) + ".json");
            string? backupPath = null;

            try
            {
                Directory.CreateDirectory(targetDir);

                // 2) backup del spec previo si existe
                if (File.Exists(path))
                {
                    var ts = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                    backupPath = path + ".bak-" + ts;
                    File.Copy(path, backupPath, overwrite: true);
                }

                // 3) escribir el .json nuevo
                File.WriteAllText(path, normalizedJson);

                // 4) reload + verificar que el plugin recien subido aparece
                _registry.Reload();
                if (!RegistryContainsPlugin(pluginId))
                {
                    Rollback(path, backupPath);
                    return new PluginUploadResult(
                        Ok: false,
                        Errors: new[] { $"plugin {pluginId} se persistio pero no aparece en el registry post-reload (revisa formato)" },
                        SavedPath: null,
                        BackupPath: null,
                        ActivePluginsCount: ActivePluginsCount());
                }

                return new PluginUploadResult(true, Array.Empty<string>(), path, backupPath, ActivePluginsCount());
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Rollback(path, backupPath);
                return new PluginUploadResult(
                    Ok: false,
                    Errors: new[] { $"fallo al guardar/recargar: {e.Message}" },
                    SavedPath: null,
                    BackupPath: null,
                    ActivePluginsCount: ActivePluginsCount());
            }
        }
    }

    public PluginUploadResult Delete(PluginKind kind, string pluginId)
    {
        lock (_gate)
        {
            var dir = DirFor(kind);
            var path = Path.Combine(dir, SafeFilename(pluginId) + ".json");

            if (!File.Exists(path))
            {
                return new PluginUploadResult(
                    Ok: false,
                    Errors: new[] { $"el plugin {pluginId} no es un override-de-disco, no se puede borrar (los embedded son built-in)" },
                    SavedPath: null,
                    BackupPath: null,
                    ActivePluginsCount: ActivePluginsCount());
            }

            try
            {
                File.Delete(path);
                _registry.Reload();
                return new PluginUploadResult(true, Array.Empty<string>(), null, null, ActivePluginsCount());
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return new PluginUploadResult(
                    Ok: false,
                    Errors: new[] { $"fallo al borrar: {e.Message}" },
                    SavedPath: null,
                    BackupPath: null,
                    ActivePluginsCount: ActivePluginsCount());
            }
        }
    }

    public PluginUploadResult Reload()
    {
        lock (_gate)
        {
            try
            {
                _registry.Reload();
                return new PluginUploadResult(true, Array.Empty<string>(), null, null, ActivePluginsCount());
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return new PluginUploadResult(
                    Ok: false,
                    Errors: new[] { $"reload fallo: {e.Message}" },
                    SavedPath: null,
                    BackupPath: null,
                    ActivePluginsCount: ActivePluginsCount());
            }
        }
    }

    // ── helpers ──

    private string DirFor(PluginKind kind) => kind == PluginKind.Lims ? _limsDir : _printDir;

    private int ActivePluginsCount() => _registry.List().Count;

    private bool RegistryContainsPlugin(string pluginId)
        // usamos AllPluginIds() (incluye print) en vez de List() que filtra print plugins
        => _registry.AllPluginIds().Any(id => string.Equals(id, pluginId, StringComparison.Ordinal));

    private static void Rollback(string path, string? backupPath)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        if (backupPath is not null && File.Exists(backupPath))
        {
            try { File.Copy(backupPath, path, overwrite: true); } catch { /* best-effort */ }
        }
    }

    private static string SafeFilename(string pluginId)
    {
        var clean = new string(pluginId.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrEmpty(clean) ? "plugin" : clean;
    }

    // valida segun el tipo: parsea + chequea campos requeridos + normaliza el JSON
    private static ValidationResult ValidateRawJson(PluginKind kind, string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return ValidationResult.Fail("body vacio");

        // 1) intentar parsear
        if (kind == PluginKind.Lims)
        {
            PacInstrumentSpec? spec;
            try { spec = JsonSerializer.Deserialize<PacInstrumentSpec>(rawJson, JsonOptions); }
            catch (JsonException e) { return ValidationResult.Fail($"JSON invalido: {e.Message}"); }

            if (spec is null) return ValidationResult.Fail("spec deserializado como null");

            var errs = new List<string>();
            if (string.IsNullOrWhiteSpace(spec.PluginId)) errs.Add("pluginId requerido");
            if (string.IsNullOrWhiteSpace(spec.AnalyzerType)) errs.Add("analyzerType requerido");
            if (string.IsNullOrWhiteSpace(spec.DisplayName)) errs.Add("displayName requerido");
            if (errs.Count > 0) return ValidationResult.Fail(errs.ToArray());

            // re-serializar normalizado para consistencia (camelCase, indentado)
            var canonical = JsonSerializer.Serialize(spec, PersistOptions);
            return ValidationResult.Pass(spec.PluginId, canonical);
        }
        else
        {
            PrintPluginSpec? spec;
            try { spec = JsonSerializer.Deserialize<PrintPluginSpec>(rawJson, JsonOptions); }
            catch (JsonException e) { return ValidationResult.Fail($"JSON invalido: {e.Message}"); }

            if (spec is null) return ValidationResult.Fail("spec deserializado como null");

            var errs = new List<string>();
            if (string.IsNullOrWhiteSpace(spec.Id)) errs.Add("id requerido");
            if (string.IsNullOrWhiteSpace(spec.AnalyzerType)) errs.Add("analyzerType requerido");
            if (string.IsNullOrWhiteSpace(spec.HeaderMarker)) errs.Add("headerMarker requerido");
            if (string.IsNullOrWhiteSpace(spec.DisplayName)) errs.Add("displayName requerido");
            if (errs.Count > 0) return ValidationResult.Fail(errs.ToArray());

            var canonical = JsonSerializer.Serialize(spec, PersistOptions);
            return ValidationResult.Pass(spec.Id, canonical);
        }
    }

    private readonly record struct ValidationResult(
        bool Ok,
        IReadOnlyList<string> Errors,
        string PluginId,
        string NormalizedJson)
    {
        public static ValidationResult Pass(string pluginId, string normalizedJson)
            => new(true, Array.Empty<string>(), pluginId, normalizedJson);
        public static ValidationResult Fail(params string[] errors)
            => new(false, errors, "", "");
    }
}
