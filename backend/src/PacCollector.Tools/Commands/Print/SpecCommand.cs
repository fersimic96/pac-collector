namespace PacCollector.Tools.Commands.Print;

// pac-tool print spec <init|test>
internal static class SpecCommand
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
            "init" => SpecInitCommand.Run(rest),
            "test" => await Task.FromResult(SpecTestCommand.Run(rest)),
            _ => throw new ArgumentException($"unknown spec subcommand '{args[0]}'. Try: pac-tool print spec --help"),
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("pac-tool print spec — gestion de specs JSON");
        Console.WriteLine();
        Console.WriteLine("SUBCOMMANDS:");
        Console.WriteLine("    init    Genera boilerplate JSON con campos minimos");
        Console.WriteLine("    test    Corre un spec contra un .bin y reporta coverage");
    }
}
