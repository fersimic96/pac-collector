using System.Net.Sockets;
using System.Text;

namespace PacCollector.MockDevice;

// pac-mock ipp send: abre TCP al target, envia POST IPP con el payload como body,
// lee la respuesta del colector. Util para validar que el modo Print/IPP del
// colector procesa correctamente las fixtures binarias capturadas.
internal static class IppCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args[0] != "send")
            throw new ArgumentException($"unknown ipp subcommand '{args[0]}'. Try: pac-mock ipp --help");

        var options = ParseOptions(args[1..]);
        return await SendAsync(options);
    }

    private static IppSendOptions ParseOptions(string[] args)
    {
        string? target = null;
        string? payload = null;
        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--target":
                    target = RequireValue(args, ref i, "--target");
                    break;
                case "--payload":
                    payload = RequireValue(args, ref i, "--payload");
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (string.IsNullOrEmpty(target))
            throw new ArgumentException("--target is required (HOST:PORT, e.g. 127.0.0.1:631)");
        if (string.IsNullOrEmpty(payload))
            throw new ArgumentException("--payload is required (path to .bin file)");

        var (host, port) = ParseTarget(target);
        if (!File.Exists(payload))
            throw new ArgumentException($"payload file not found: {payload}");

        return new IppSendOptions(host, port, payload);
    }

    private static async Task<int> SendAsync(IppSendOptions opts)
    {
        var body = await File.ReadAllBytesAsync(opts.PayloadPath);
        Console.WriteLine($"pac-mock ipp: connecting to {opts.Host}:{opts.Port}");
        Console.WriteLine($"pac-mock ipp: payload {opts.PayloadPath} ({body.Length} bytes)");

        using var client = new TcpClient();
        await client.ConnectAsync(opts.Host, opts.Port);
        await using var stream = client.GetStream();

        // POST IPP/1.1 con body = print payload binario.
        // El colector clasifica como HTTP por el "POST " prefix; igualmente acepta raw
        // si el primer chunk no parece HTTP, asi que la fixture FZP/CPP/PMD/Dist2 pasa OK.
        var headers = new StringBuilder();
        headers.Append("POST /ipp HTTP/1.1\r\n");
        headers.Append($"Host: {opts.Host}:{opts.Port}\r\n");
        headers.Append("Content-Type: application/ipp\r\n");
        headers.Append($"Content-Length: {body.Length}\r\n");
        headers.Append("User-Agent: pac-mock/0.1\r\n");
        headers.Append("Connection: close\r\n");
        headers.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
        client.Client.Shutdown(SocketShutdown.Send);

        // leer respuesta del colector (best-effort, timeout corto)
        var response = new MemoryStream();
        var buf = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                var n = await stream.ReadAsync(buf, cts.Token);
                if (n == 0) break;
                response.Write(buf, 0, n);
            }
        }
        catch (OperationCanceledException) { /* timeout — colector puede cerrar sin responder */ }

        Console.WriteLine($"pac-mock ipp: server response {response.Length} bytes");
        if (response.Length > 0)
        {
            var preview = Encoding.ASCII.GetString(response.ToArray(), 0, (int)Math.Min(response.Length, 200));
            Console.WriteLine($"pac-mock ipp: preview: {preview.Replace('\0', '.')}");
        }
        return 0;
    }

    private static (string Host, int Port) ParseTarget(string target)
    {
        var colon = target.LastIndexOf(':');
        if (colon <= 0 || colon == target.Length - 1)
            throw new ArgumentException("--target must be HOST:PORT");
        var host = target[..colon];
        if (!int.TryParse(target[(colon + 1)..], out var port) || port is < 1 or > 65535)
            throw new ArgumentException($"invalid port in --target '{target}'");
        return (host, port);
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
        Console.WriteLine("pac-mock ipp send — send IPP print job to a target");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    pac-mock ipp send --target HOST:PORT --payload FILE.bin");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --target HOST:PORT    Where to send (typically 127.0.0.1:631 for local pac-collector)");
        Console.WriteLine("    --payload FILE.bin    Path to the binary print payload (IPP request or raw PCL)");
    }
}

internal sealed record IppSendOptions(string Host, int Port, string PayloadPath);
