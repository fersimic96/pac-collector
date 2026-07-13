using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PacCollector.Domain.Errors;

namespace PacCollector.Infrastructure.Plugins.Print;

// carga PrintPluginSpec desde JSON. Tres fuentes en orden:
//   1) override en disco: <pluginsDir>/print/*.json
//   2) embedded resources del assembly: PacCollector.Infrastructure.Plugins.Print.Specs.*.json
// dejar override en disco permite agregar/cambiar equipos sin recompilar.
public static class PrintPluginSpecLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static IReadOnlyList<PrintPluginSpec> LoadAll(string? overrideDir = null)
    {
        var specs = new Dictionary<string, PrintPluginSpec>(StringComparer.Ordinal);

        foreach (var spec in LoadEmbedded())
            specs[spec.Id] = spec;

        if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
            foreach (var spec in LoadFromDirectory(overrideDir))
                specs[spec.Id] = spec;

        return specs.Values.ToList();
    }

    // carga UN spec desde un path explicito. Util para herramientas CLI que
    // validan un spec aislado contra un fixture sin pasar por LoadAll.
    // Strict: cualquier error parsea/valida tira ConfigInvalidException.
    public static PrintPluginSpec LoadFromFile(string path)
    {
        var raw = File.ReadAllText(path);
        var spec = Parse(raw, source: path);
        if (spec is null)
            throw new ConfigInvalidException("print_plugin_spec", $"{path}: empty or null spec");
        return spec;
    }

    private static IEnumerable<PrintPluginSpec> LoadEmbedded()
    {
        var asm = typeof(PrintPluginSpecLoader).Assembly;
        const string prefix = "PacCollector.Infrastructure.Plugins.Print.Specs.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var raw = reader.ReadToEnd();
            var spec = Parse(raw, source: name);
            if (spec is not null) yield return spec;
        }
    }

    // override dir es tolerante a JSON malo: log warning + skip, no crashea
    // el boot. Embedded resources siguen siendo strict (un .json embebido
    // malformado significa un build malo, debe fallar visiblemente).
    private static IEnumerable<PrintPluginSpec> LoadFromDirectory(string dir)
    {
        foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[print-plugin-loader] skipping {path}: read error ({e.Message})");
                continue;
            }

            PrintPluginSpec? spec;
            try { spec = Parse(raw, source: path); }
            catch (ConfigInvalidException e)
            {
                Console.Error.WriteLine($"[print-plugin-loader] skipping {path}: {e.Message}");
                continue;
            }
            if (spec is not null) yield return spec;
        }
    }

    private static PrintPluginSpec? Parse(string json, string source)
    {
        try
        {
            var spec = JsonSerializer.Deserialize<PrintPluginSpec>(json, JsonOptions);
            if (spec is null) return null;
            ValidateOrThrow(spec, source);
            return spec;
        }
        catch (JsonException e)
        {
            throw new ConfigInvalidException("print_plugin_spec", $"failed to parse {source}: {e.Message}");
        }
    }

    private static void ValidateOrThrow(PrintPluginSpec spec, string source)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(spec.Id)) errors.Add("id is required");
        if (string.IsNullOrWhiteSpace(spec.AnalyzerType)) errors.Add("analyzerType is required");
        if (string.IsNullOrWhiteSpace(spec.HeaderMarker)) errors.Add("headerMarker is required");
        if (errors.Count > 0)
            throw new ConfigInvalidException("print_plugin_spec", $"{source}: {string.Join("; ", errors)}");
    }
}
