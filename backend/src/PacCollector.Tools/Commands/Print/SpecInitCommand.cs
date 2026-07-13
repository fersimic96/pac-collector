using System.Text.Json;
using System.Text.Json.Serialization;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Tools.Commands.Print;

// pac-tool print spec init: emite un JSON spec con campos minimos completos
// y arrays vacios para que el integrador rellene. Setea defaults sensatos
// segun el Kind elegido (ej. requiresCrRender=true si kind=optiDist).
internal static class SpecInitCommand
{
    // mismo perfil JSON que PrintPluginSpecLoader: camelCase + enum como string.
    // Critico — sin el JsonStringEnumConverter el kind sale como int (0/1/2) y
    // el loader le rechaza al cargar.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var opts = ParseOptions(args);
        var spec = BuildSpec(opts);
        var json = JsonSerializer.Serialize(spec, JsonOptions);

        if (opts.OutputPath is null)
            Console.WriteLine(json);
        else
        {
            File.WriteAllText(opts.OutputPath, json);
            Console.WriteLine($"pac-tool spec init: escrito {opts.OutputPath}");
        }
        return 0;
    }

    private static SpecInitOptions ParseOptions(string[] args)
    {
        string? analyzerType = null;
        string? headerMarker = null;
        string? kindStr = null;
        string? output = null;
        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--analyzer-type":
                    analyzerType = RequireValue(args, ref i, "--analyzer-type");
                    break;
                case "--header-marker":
                    headerMarker = RequireValue(args, ref i, "--header-marker");
                    break;
                case "--kind":
                    kindStr = RequireValue(args, ref i, "--kind");
                    break;
                case "--output":
                    output = RequireValue(args, ref i, "--output");
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (string.IsNullOrEmpty(analyzerType))
            throw new ArgumentException("--analyzer-type is required");
        if (string.IsNullOrEmpty(headerMarker))
            throw new ArgumentException("--header-marker is required");
        if (string.IsNullOrEmpty(kindStr))
            throw new ArgumentException("--kind is required (labelValue | distillation | optiDist)");

        if (!Enum.TryParse<PrintReportKind>(kindStr, ignoreCase: true, out var kind))
            throw new ArgumentException($"--kind invalido '{kindStr}'. Opciones: labelValue, distillation, optiDist");

        return new SpecInitOptions(analyzerType, headerMarker, kind, output);
    }

    private static PrintPluginSpec BuildSpec(SpecInitOptions opts)
    {
        var lowerType = opts.AnalyzerType.ToLowerInvariant();
        var pluginId = $"{lowerType}-print";

        return new PrintPluginSpec
        {
            Id = pluginId,
            DisplayName = $"PAC {opts.AnalyzerType} (modo Print/Iris)",
            AnalyzerType = opts.AnalyzerType,
            Vendor = "PAC Collector",
            Version = "0.1.0",
            Kind = opts.Kind,
            HeaderMarker = opts.HeaderMarker,
            HeaderRegexOverride = opts.Kind == PrintReportKind.OptiDist
                ? $"{opts.HeaderMarker}\\s+(\\d+)"  // OptiDist headers no tienen "V <fw>"
                : null,
            HeadlineLabel = "",  // el integrador completa
            RequiresCrRender = opts.Kind == PrintReportKind.OptiDist,
            ExtraFieldKeys = new(),  // el integrador agrega mappings label-based o pattern-based
            FieldSpecs = new(),      // el integrador agrega metadata UI
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{flag} requires a value");
        var v = args[i + 1];
        i += 2;
        return v;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("pac-tool print spec init — genera boilerplate JSON spec");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    pac-tool print spec init --analyzer-type T --header-marker M --kind K [--output FILE.json]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --analyzer-type T       Nombre del equipo (ej. OptiFlash)");
        Console.WriteLine("    --header-marker M       Marker en el banner del reporte (ej. OptiFlash)");
        Console.WriteLine("    --kind K                labelValue | distillation | optiDist");
        Console.WriteLine("    --output FILE.json      Path destino (default: stdout)");
        Console.WriteLine();
        Console.WriteLine("Si kind=optiDist, setea requiresCrRender=true y headerRegexOverride apropiado.");
        Console.WriteLine("El integrador despues agrega extraFieldKeys (label-based o pattern-based)");
        Console.WriteLine("y itera con 'spec test' hasta coverage 100%.");
    }
}

internal sealed record SpecInitOptions(
    string AnalyzerType,
    string HeaderMarker,
    PrintReportKind Kind,
    string? OutputPath);
