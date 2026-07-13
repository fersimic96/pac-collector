using System.Text.Json;
using PacCollector.Domain.Errors;

namespace PacCollector.Infrastructure.Hotfolder;

// carga HotfolderTemplate desde JSON. Tres fuentes en orden:
//   1) embedded resources del assembly: PacCollector.Infrastructure.Hotfolder.Templates.*.json
//   2) override en disco: <overrideDir>/*.json (pisa por Name)
// Override es tolerante a JSON malo (skip + log); embedded es strict.
public static class HotfolderTemplateLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<HotfolderTemplate> LoadAll(string? overrideDir = null)
    {
        var templates = new Dictionary<string, HotfolderTemplate>(StringComparer.Ordinal);

        foreach (var tpl in LoadEmbedded())
            templates[tpl.Name] = tpl;

        if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
            foreach (var tpl in LoadFromDirectory(overrideDir))
                templates[tpl.Name] = tpl;

        return templates.Values.ToList();
    }

    public static HotfolderTemplate LoadFromFile(string path)
    {
        var raw = File.ReadAllText(path);
        var tpl = Parse(raw, source: path);
        if (tpl is null)
            throw new ConfigInvalidException("hotfolder_template", $"{path}: empty or null template");
        return tpl;
    }

    private static IEnumerable<HotfolderTemplate> LoadEmbedded()
    {
        var asm = typeof(HotfolderTemplateLoader).Assembly;
        const string prefix = "PacCollector.Infrastructure.Hotfolder.Templates.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var tpl = Parse(reader.ReadToEnd(), source: name);
            if (tpl is not null) yield return tpl;
        }
    }

    // mismo patron que los loaders de specs: tolerante a basura en override dir
    private static IEnumerable<HotfolderTemplate> LoadFromDirectory(string dir)
    {
        foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[hotfolder-template-loader] skipping {path}: read error ({e.Message})");
                continue;
            }

            HotfolderTemplate? tpl;
            try { tpl = Parse(raw, source: path); }
            catch (ConfigInvalidException e)
            {
                Console.Error.WriteLine($"[hotfolder-template-loader] skipping {path}: {e.Message}");
                continue;
            }
            if (tpl is not null) yield return tpl;
        }
    }

    private static HotfolderTemplate? Parse(string json, string source)
    {
        try
        {
            var tpl = JsonSerializer.Deserialize<HotfolderTemplate>(json, JsonOptions);
            if (tpl is null) return null;
            ValidateOrThrow(tpl, source);
            return tpl;
        }
        catch (JsonException e)
        {
            throw new ConfigInvalidException("hotfolder_template", $"failed to parse {source}: {e.Message}");
        }
    }

    private static void ValidateOrThrow(HotfolderTemplate tpl, string source)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(tpl.Name)) errors.Add("name is required");
        if (string.IsNullOrWhiteSpace(tpl.FilenameTemplate)) errors.Add("filenameTemplate is required");
        if (tpl.Lines.Count == 0) errors.Add("lines must have at least one entry");
        if (!string.Equals(tpl.LineEnding, "LF", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tpl.LineEnding, "CRLF", StringComparison.OrdinalIgnoreCase))
            errors.Add($"lineEnding must be 'LF' or 'CRLF' (got '{tpl.LineEnding}')");
        if (errors.Count > 0)
            throw new ConfigInvalidException("hotfolder_template", $"{source}: {string.Join("; ", errors)}");
    }
}
