using PacCollector.MockDevice;

// pac-mock — simulador de equipo PAC desde linea de comandos.
// Implementa el lado-equipo de dos protocolos:
//   - ipp send: protocolo IPP estandar (RFC 8010) hacia colector como impresora
//   - lims send: protocolo proprietary (UDP beacon + ACK + TCP JSON + NUL)
//
// El codigo de este binario es la spec ejecutable de los protocolos, referenciada
// desde docs/protocols/*.md como "implementacion canonica del lado-equipo".
//
// Uso:
//   pac-mock ipp send --target HOST:PORT --payload FILE
//   pac-mock lims send --target HOST --json FILE [--udp-port 3000] [--timeout-ms 5000]
//   pac-mock --help

try
{
    if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
    {
        PrintUsage();
        return 0;
    }

    var rest = args[1..];
    return args[0] switch
    {
        "ipp" => await IppCommand.RunAsync(rest),
        "lims" => await LimsCommand.RunAsync(rest),
        _ => Fail($"unknown command '{args[0]}'. Try --help"),
    };
}
catch (ArgumentException ex)
{
    return Fail(ex.Message);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"pac-mock: error: {ex.Message}");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("pac-mock — PAC equipment simulator");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("    pac-mock <command> <subcommand> [options]");
    Console.WriteLine();
    Console.WriteLine("COMMANDS:");
    Console.WriteLine("    ipp send    Send an IPP print job to a target (collector as printer).");
    Console.WriteLine("    lims send   Simulate full LIMS Ethernet handshake (UDP beacon + ACK + TCP JSON).");
    Console.WriteLine();
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine("    pac-mock ipp send --target 127.0.0.1:631 --payload optipmd.bin");
    Console.WriteLine("    pac-mock lims send --target 127.0.0.1 --json sample.json");
    Console.WriteLine();
    Console.WriteLine("Run 'pac-mock <command> --help' for details on a specific command.");
}

static int Fail(string msg)
{
    Console.Error.WriteLine($"pac-mock: {msg}");
    return 64;  // EX_USAGE
}
