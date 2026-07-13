using System.Net;
using System.Net.Sockets;

namespace PacCollector.Tools.Commands.Print;

// pac-tool print capture: bindea TCP en un puerto, acepta UNA conexion,
// drena los bytes hasta close, guarda el blob a archivo. Modo passthrough
// para que el integrador capture el payload real de un equipo sin que el
// colector procese nada todavia.
internal static class CaptureCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var opts = ParseOptions(args);
        return await CaptureAsync(opts);
    }

    private static CaptureOptions ParseOptions(string[] args)
    {
        var port = 6310;
        string? output = null;
        int? timeoutSec = null;
        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--port":
                    port = int.Parse(RequireValue(args, ref i, "--port"));
                    break;
                case "--output":
                    output = RequireValue(args, ref i, "--output");
                    break;
                case "--timeout-sec":
                    timeoutSec = int.Parse(RequireValue(args, ref i, "--timeout-sec"));
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (string.IsNullOrEmpty(output))
            throw new ArgumentException("--output is required (path al archivo .bin destino)");
        if (port is < 1 or > 65535)
            throw new ArgumentException($"--port out of range: {port}");
        return new CaptureOptions(port, output, timeoutSec);
    }

    private static async Task<int> CaptureAsync(CaptureOptions opts)
    {
        var listener = new TcpListener(IPAddress.Any, opts.Port);
        listener.Start();
        Console.WriteLine($"pac-tool capture: escuchando TCP :{opts.Port} (esperando 1 conexion)");
        if (opts.TimeoutSec is int t)
            Console.WriteLine($"pac-tool capture: timeout {t}s");
        Console.WriteLine($"pac-tool capture: configurar el equipo para 'Print over Ethernet' apuntando a esta IP:{opts.Port}");

        try
        {
            using var cts = opts.TimeoutSec is int sec
                ? new CancellationTokenSource(TimeSpan.FromSeconds(sec))
                : new CancellationTokenSource();

            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"pac-tool capture: timeout esperando conexion");
                return 3;
            }

            using (client)
            {
                var remote = client.Client.RemoteEndPoint;
                Console.WriteLine($"pac-tool capture: conexion entrante desde {remote}");

                await using var stream = client.GetStream();
                await using var fileOut = File.Create(opts.OutputPath);
                var total = 0L;
                var buf = new byte[16_384];
                while (true)
                {
                    var n = await stream.ReadAsync(buf);
                    if (n == 0) break;
                    await fileOut.WriteAsync(buf.AsMemory(0, n));
                    total += n;
                }
                Console.WriteLine($"pac-tool capture: guardado {total} bytes en {opts.OutputPath}");
                return 0;
            }
        }
        finally
        {
            listener.Stop();
        }
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
        Console.WriteLine("pac-tool print capture — captura una conexion TCP al .bin");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    pac-tool print capture --output FILE.bin [--port 6310] [--timeout-sec N]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --port PORT          Puerto TCP donde escuchar (default 6310)");
        Console.WriteLine("    --output FILE.bin    Path destino donde guardar el blob");
        Console.WriteLine("    --timeout-sec N      Timeout esperando conexion (default: sin timeout)");
        Console.WriteLine();
        Console.WriteLine("Despues de armar el spec con 'spec test', usar el .bin como fixture para tests");
        Console.WriteLine("de paridad si correponde.");
    }
}

internal sealed record CaptureOptions(int Port, string OutputPath, int? TimeoutSec);
