using PacCollector.Tools.Commands.Print;

// pac-tool — CLI de autoria para integradores de plugins print.
// Cubre el ciclo de onboarding de un equipo nuevo sin tocar C#:
//   1. capture: capturar payload real del equipo en .bin
//   2. decode:  inspeccionar el contenido textual del .bin
//   3. spec init: bootstrapear un JSON spec con campos minimos
//   4. spec test: validar el spec contra el .bin, reportar coverage
//
// El integrador ejecuta este flujo + edita el JSON spec hasta coverage 100%,
// despues lo deploya en DataDir/plugins/print/.
//
// Args parseados a mano (mismo patron que pac-mock; sin System.CommandLine
// para evitar dependency en dos developer tools chicos).

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
        "print" => await PrintCommand.RunAsync(rest),
        _ => Fail($"unknown command '{args[0]}'. Try --help"),
    };
}
catch (ArgumentException ex)
{
    return Fail(ex.Message);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"pac-tool: error: {ex.Message}");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("pac-tool — CLI de autoria para plugins print de PAC Collector");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("    pac-tool <command> <subcommand> [options]");
    Console.WriteLine();
    Console.WriteLine("COMMANDS:");
    Console.WriteLine("    print capture     Captura un payload real (1 conexion TCP) a un .bin");
    Console.WriteLine("    print decode      Decodifica un .bin a texto (con/sin strip PCL)");
    Console.WriteLine("    print spec init   Genera boilerplate JSON spec para un equipo nuevo");
    Console.WriteLine("    print spec test   Corre un spec contra un .bin y reporta coverage de campos");
    Console.WriteLine();
    Console.WriteLine("EJEMPLO DE FLUJO DE ONBOARDING:");
    Console.WriteLine("    pac-tool print capture --port 6310 --output /tmp/optiflash-001.bin");
    Console.WriteLine("    pac-tool print decode --input /tmp/optiflash-001.bin --strip-pcl");
    Console.WriteLine("    pac-tool print spec init --analyzer-type OptiFlash --kind labelValue \\");
    Console.WriteLine("                             --header-marker OptiFlash --output optiflash.json");
    Console.WriteLine("    # [editar optiflash.json con extraFieldKeys]");
    Console.WriteLine("    pac-tool print spec test --spec optiflash.json --sample /tmp/optiflash-001.bin");
    Console.WriteLine();
    Console.WriteLine("Run 'pac-tool <command> --help' para detalle.");
}

static int Fail(string msg)
{
    Console.Error.WriteLine($"pac-tool: {msg}");
    return 64;  // EX_USAGE
}
