using System.Reflection;
using System.Text.Json;
using PacCollector.Domain.Errors;

namespace PacCollector.Infrastructure.Plugins.Builtin;

// carga PacInstrumentSpec desde JSON. Dos fuentes en orden:
//   1) override en disco: <pluginsDir>/lims/*.json
//   2) embedded resources: PacCollector.Infrastructure.Plugins.Builtin.Specs.*.json
// override en disco permite agregar/cambiar equipos sin recompilar.
public static class PacInstrumentSpecLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<PacInstrumentSpec> LoadAll(string? overrideDir = null)
    {
        var specs = new Dictionary<string, PacInstrumentSpec>(StringComparer.Ordinal);

        foreach (var spec in LoadEmbedded())
            specs[spec.PluginId] = spec;

        if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
            foreach (var spec in LoadFromDirectory(overrideDir))
                specs[spec.PluginId] = spec;

        return specs.Values.ToList();
    }

    private static IEnumerable<PacInstrumentSpec> LoadEmbedded()
    {
        var asm = typeof(PacInstrumentSpecLoader).Assembly;
        const string prefix = "PacCollector.Infrastructure.Plugins.Builtin.Specs.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var spec = Parse(reader.ReadToEnd(), source: name);
            if (spec is not null) yield return spec;
        }
    }

    // override dir es tolerante a JSON malo: log warning + skip, no crashea
    // el boot. Embedded resources siguen siendo strict (un .json embebido
    // malformado significa un build malo, debe fallar visiblemente).
    private static IEnumerable<PacInstrumentSpec> LoadFromDirectory(string dir)
    {
        foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[lims-plugin-loader] skipping {path}: read error ({e.Message})");
                continue;
            }

            PacInstrumentSpec? spec;
            try { spec = Parse(raw, source: path); }
            catch (ConfigInvalidException e)
            {
                Console.Error.WriteLine($"[lims-plugin-loader] skipping {path}: {e.Message}");
                continue;
            }
            if (spec is not null) yield return spec;
        }
    }

    private static PacInstrumentSpec? Parse(string json, string source)
    {
        try
        {
            var spec = JsonSerializer.Deserialize<PacInstrumentSpec>(json, JsonOptions);
            if (spec is null) return null;
            ValidateOrThrow(spec, source);
            return spec;
        }
        catch (JsonException e)
        {
            throw new ConfigInvalidException("lims_plugin_spec", $"failed to parse {source}: {e.Message}");
        }
    }

    private static void ValidateOrThrow(PacInstrumentSpec spec, string source)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(spec.PluginId)) errors.Add("pluginId is required");
        if (string.IsNullOrWhiteSpace(spec.AnalyzerType)) errors.Add("analyzerType is required");
        if (errors.Count > 0)
            throw new ConfigInvalidException("lims_plugin_spec", $"{source}: {string.Join("; ", errors)}");
    }
}
