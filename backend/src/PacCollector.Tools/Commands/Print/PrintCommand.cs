namespace PacCollector.Tools.Commands.Print;

// dispatcher de `pac-tool print <subcommand>`.
internal static class PrintCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var rest = args[1..];
        return args[0] switch
        {
            "capture" => await CaptureCommand.RunAsync(rest),
            "decode" => DecodeCommand.Run(rest),
            "spec" => await SpecCommand.RunAsync(rest),
            _ => throw new ArgumentException($"unknown print subcommand '{args[0]}'. Try: pac-tool print --help"),
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("pac-tool print — herramientas de autoria para plugins de modo print");
        Console.WriteLine();
        Console.WriteLine("SUBCOMMANDS:");
        Console.WriteLine("    capture     Captura un payload real (1 conexion TCP) a un .bin");
        Console.WriteLine("    decode      Decodifica un .bin a texto (con/sin strip PCL)");
        Console.WriteLine("    spec init   Genera boilerplate JSON spec");
        Console.WriteLine("    spec test   Corre un spec contra un .bin y reporta coverage");
    }
}
