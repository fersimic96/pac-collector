using System.Text;
using PacCollector.Domain.Entities;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Tools.Commands.Print;

// pac-tool print spec test: carga un spec, corre el plugin contra un sample
// binario, y reporta:
//   - campos typed extraidos (Serial, IBP, FBP, recovery, residue, etc.)
//   - mappings de extraFieldKeys que matchearon y los que no (con coverage)
//   - HeaderMarker match status
//   - errores de validacion (header no encontrado, malformed, etc.)
//
// El integrador itera spec + 'spec test' hasta cubrir todos los campos
// que le interesan al cliente.
internal static class SpecTestCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var opts = ParseOptions(args);

        PrintPluginSpec spec;
        try
        {
            spec = PrintPluginSpecLoader.LoadFromFile(opts.SpecPath);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"pac-tool spec test: fallo cargar spec: {e.Message}");
            return 5;
        }
        Console.WriteLine($"Spec: {spec.Id} (kind={spec.Kind}, headerMarker=\"{spec.HeaderMarker}\")");
        Console.WriteLine($"Sample: {opts.SamplePath}");
        Console.WriteLine();

        var bytes = File.ReadAllBytes(opts.SamplePath);
        var plugin = new ConfigurablePrintPlugin(spec);

        if (!plugin.AcceptsPrintFormat(bytes))
        {
            Console.WriteLine($"✗ AcceptsPrintFormat: HeaderMarker \"{spec.HeaderMarker}\" NO encontrado en los primeros 8KB");
            Console.WriteLine();
            Console.WriteLine("Probable: el spec no es para este equipo, o el HeaderMarker esta mal.");
            return 4;
        }
        Console.WriteLine($"✓ AcceptsPrintFormat: HeaderMarker \"{spec.HeaderMarker}\" detectado");

        Sample sample;
        try
        {
            sample = plugin.ParsePrintMessage(bytes, sourceIp: null, DateTimeOffset.UtcNow);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"✗ ParsePrintMessage tiro: {e.Message}");
            return 3;
        }
        Console.WriteLine($"✓ ParsePrintMessage OK");
        Console.WriteLine();

        PrintTypedFields(sample);
        Console.WriteLine();
        PrintExtraCoverage(spec, sample);
        return 0;
    }

    private static void PrintTypedFields(Sample s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Typed fields:");
        sb.AppendLine($"  serial            = {Display(s.Serial.AsString)}");
        sb.AppendLine($"  analyzerType      = {Display(s.AnalyzerType)}");
        sb.AppendLine($"  sampleIdentifier  = {Display(s.SampleIdentifier)}");
        sb.AppendLine($"  operator          = {Display(s.Operator)}");
        sb.AppendLine($"  program           = {Display(s.Program)}");
        sb.AppendLine($"  startAt           = {Display(s.StartAt)}");
        sb.AppendLine($"  endAt             = {Display(s.EndAt)}");
        sb.AppendLine($"  ibp               = {Display(s.Ibp)}");
        sb.AppendLine($"  fbp               = {Display(s.Fbp)}");
        sb.AppendLine($"  residue           = {Display(s.Residue)}");
        sb.AppendLine($"  recovery          = {Display(s.Recovery)}");
        sb.AppendLine($"  endOfTest         = {Display(s.EndOfTest)}");
        sb.AppendLine($"  curve.count       = {s.Curve.Count}");
        Console.Write(sb);
    }

    private static void PrintExtraCoverage(PrintPluginSpec spec, Sample sample)
    {
        Console.WriteLine($"Extra fields coverage ({spec.ExtraFieldKeys.Count} mappings declarados):");
        if (spec.ExtraFieldKeys.Count == 0)
        {
            Console.WriteLine("  (no hay mappings declarados — agregar extraFieldKeys al spec)");
        }
        var matched = 0;
        foreach (var map in spec.ExtraFieldKeys)
        {
            var mode = string.IsNullOrEmpty(map.Pattern) ? "label" : "regex";
            if (sample.Extra.TryGetValue(map.Key, out var v) && !string.IsNullOrEmpty(v))
            {
                Console.WriteLine($"  ✓ {map.Key,-24} [{mode,-5}] = {Truncate(v, 60)}");
                matched++;
            }
            else
            {
                var hint = mode == "label" ? $"label=\"{map.Label}\"" : $"pattern=\"{Truncate(map.Pattern!, 50)}\"";
                Console.WriteLine($"  ✗ {map.Key,-24} [{mode,-5}] no match — {hint}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Resumen: {matched}/{spec.ExtraFieldKeys.Count} mappings matched");

        // tambien listar fields en Extra que NO estan declarados (puede ser info util)
        var declared = new HashSet<string>(spec.ExtraFieldKeys.Select(m => m.Key), StringComparer.Ordinal);
        var undeclared = sample.Extra.Keys
            .Where(k => !declared.Contains(k) && k != "hpgl_curve")
            .ToList();
        if (undeclared.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Fields presentes en Extra pero NO declarados en spec ({undeclared.Count}):");
            foreach (var k in undeclared)
                Console.WriteLine($"    {k} = {Truncate(sample.Extra[k], 60)}");
        }
    }

    private static SpecTestOptions ParseOptions(string[] args)
    {
        string? spec = null;
        string? sample = null;
        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--spec":
                    spec = RequireValue(args, ref i, "--spec");
                    break;
                case "--sample":
                    sample = RequireValue(args, ref i, "--sample");
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (string.IsNullOrEmpty(spec)) throw new ArgumentException("--spec is required");
        if (string.IsNullOrEmpty(sample)) throw new ArgumentException("--sample is required");
        if (!File.Exists(spec)) throw new ArgumentException($"spec file not found: {spec}");
        if (!File.Exists(sample)) throw new ArgumentException($"sample file not found: {sample}");
        return new SpecTestOptions(spec, sample);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{flag} requires a value");
        var v = args[i + 1];
        i += 2;
        return v;
    }

    private static string Display(object? v) => v switch
    {
        null => "(null)",
        string s when s.Length == 0 => "(empty)",
        DateTimeOffset dt => dt.ToString("o"),
        _ => v.ToString() ?? "(?)",
    };

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static void PrintUsage()
    {
        Console.WriteLine("pac-tool print spec test — valida un spec contra un sample");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    pac-tool print spec test --spec SPEC.json --sample SAMPLE.bin");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --spec SPEC.json      Path al spec JSON a validar");
        Console.WriteLine("    --sample SAMPLE.bin   Path al payload capturado");
        Console.WriteLine();
        Console.WriteLine("EXIT CODES:");
        Console.WriteLine("    0   spec OK + parseo OK + (info de coverage por stdout)");
        Console.WriteLine("    3   ParsePrintMessage tiro excepcion");
        Console.WriteLine("    4   HeaderMarker no encontrado en payload");
        Console.WriteLine("    5   spec JSON invalido");
        Console.WriteLine("    64  argumentos invalidos");
    }
}

internal sealed record SpecTestOptions(string SpecPath, string SamplePath);
