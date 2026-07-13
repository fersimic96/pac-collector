using System.Text;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Tools.Commands.Print;

// pac-tool print decode: lee un .bin, lo decodifica UTF-8 (igual que el colector),
// opcionalmente aplica PclStripper y/o CrOverwriteRenderer, output a stdout.
// Util para que el integrador inspeccione el contenido textual del payload
// antes de armar el spec.
internal static class DecodeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var opts = ParseOptions(args);
        var bytes = File.ReadAllBytes(opts.InputPath);
        var text = Encoding.UTF8.GetString(bytes);
        if (opts.StripPcl) text = PclStripper.Strip(text);
        if (opts.CrRender) text = CrOverwriteRenderer.Render(text);
        Console.Write(text);
        return 0;
    }

    private static DecodeOptions ParseOptions(string[] args)
    {
        string? input = null;
        var stripPcl = false;
        var crRender = false;
        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--input":
                    input = RequireValue(args, ref i, "--input");
                    break;
                case "--strip-pcl":
                    stripPcl = true;
                    i++;
                    break;
                case "--cr-render":
                    crRender = true;
                    i++;
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("--input is required (path al .bin)");
        if (!File.Exists(input))
            throw new ArgumentException($"input file not found: {input}");
        return new DecodeOptions(input, stripPcl, crRender);
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
        Console.WriteLine("pac-tool print decode — decodifica un .bin a texto");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    pac-tool print decode --input FILE.bin [--strip-pcl] [--cr-render]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --input FILE.bin    Path al payload capturado");
        Console.WriteLine("    --strip-pcl         Aplica PclStripper (saca escapes PCL + HP-GL block)");
        Console.WriteLine("    --cr-render         Aplica CrOverwriteRenderer (Windows printer two-column)");
        Console.WriteLine();
        Console.WriteLine("Tipico flujo: --strip-pcl para ver el texto del reporte; agregar --cr-render");
        Console.WriteLine("solo para equipos tipo OptiDist2 que usan layout CR-overwrite.");
    }
}

internal sealed record DecodeOptions(string InputPath, bool StripPcl, bool CrRender);
