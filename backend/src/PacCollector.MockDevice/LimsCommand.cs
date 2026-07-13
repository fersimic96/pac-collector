using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PacCollector.MockDevice;

// pac-mock lims send: implementa el lado-EQUIPO del protocolo LIMS Ethernet
// proprietary de PAC, paso por paso:
//
//   1. Bind UDP en puerto efimero local.
//   2. Envia beacon UDP [0x01, 0x02, 0x03] -> target:3000.
//   3. Espera ACK del colector en el socket UDP local (timeout configurable).
//   4. Parsea "ACK <ip> <port>" del payload.
//   5. Abre TCP a ese ip:port.
//   6. Envia JSON UTF-8 + byte 0x00 (NUL terminator).
//   7. Lee respuesta JSON {Error, SaveCheckSum}.
//   8. Reporta resultado.
//
// Este codigo es la spec ejecutable del protocolo. Si docs/protocols/lims-ethernet.md
// diverge de esto, el codigo es la fuente de verdad.
internal static class LimsCommand
{
    private static readonly byte[] Beacon = [0x01, 0x02, 0x03];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args[0] != "send")
            throw new ArgumentException($"unknown lims subcommand '{args[0]}'. Try: pac-mock lims --help");

        var opts = ParseOptions(args[1..]);
        return await SendAsync(opts);
    }

    private static LimsSendOptions ParseOptions(string[] args)
    {
        string? target = null;
        string? json = null;
        var udpPort = 3000;
        var timeoutMs = 5000;

        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--target":
                    target = RequireValue(args, ref i, "--target");
                    break;
                case "--json":
                    json = RequireValue(args, ref i, "--json");
                    break;
                case "--udp-port":
                    udpPort = int.Parse(RequireValue(args, ref i, "--udp-port"));
                    break;
                case "--timeout-ms":
                    timeoutMs = int.Parse(RequireValue(args, ref i, "--timeout-ms"));
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        if (string.IsNullOrEmpty(target))
            throw new ArgumentException("--target is required (HOST or IP, e.g. 127.0.0.1)");
        if (string.IsNullOrEmpty(json))
            throw new ArgumentException("--json is required (path to JSON sample file)");
        if (!File.Exists(json))
            throw new ArgumentException($"JSON file not found: {json}");

        return new LimsSendOptions(target, json, udpPort, timeoutMs);
    }

    private static async Task<int> SendAsync(LimsSendOptions opts)
    {
        var payload = await File.ReadAllBytesAsync(opts.JsonPath);
        Console.WriteLine($"pac-mock lims: payload {opts.JsonPath} ({payload.Length} bytes)");
        Console.WriteLine($"pac-mock lims: target {opts.Target}:{opts.UdpPort} (UDP beacon)");

        // 1. socket UDP en puerto efimero local
        using var udp = new UdpClient(0);
        udp.Client.ReceiveTimeout = opts.TimeoutMs;
        var localPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        Console.WriteLine($"pac-mock lims: bound UDP local :{localPort}");

        // 2. resolver IP destino y mandar beacon
        var targetAddr = await ResolveTargetAsync(opts.Target);
        Console.WriteLine($"pac-mock lims: resolved target {targetAddr}");
        await udp.SendAsync(Beacon, new IPEndPoint(targetAddr, opts.UdpPort));
        Console.WriteLine($"pac-mock lims: sent beacon [0x01 0x02 0x03] -> {targetAddr}:{opts.UdpPort}");

        // 3. esperar ACK (con timeout)
        using var cts = new CancellationTokenSource(opts.TimeoutMs);
        UdpReceiveResult ackResult;
        try
        {
            ackResult = await udp.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"pac-mock lims: timeout waiting for ACK after {opts.TimeoutMs}ms");
            return 3;
        }
        var ackText = Encoding.UTF8.GetString(ackResult.Buffer);
        Console.WriteLine($"pac-mock lims: received {ackResult.Buffer.Length} bytes from {ackResult.RemoteEndPoint}: \"{ackText}\"");

        // 4. parsear "ACK <ip> <port>"
        var m = Regex.Match(ackText, @"^ACK\s+(\S+)\s+(\d+)\s*$");
        if (!m.Success)
        {
            Console.Error.WriteLine($"pac-mock lims: response not in expected 'ACK <ip> <port>' format");
            return 4;
        }
        var tcpHost = m.Groups[1].Value;
        var tcpPort = int.Parse(m.Groups[2].Value);
        Console.WriteLine($"pac-mock lims: parsed ACK -> TCP {tcpHost}:{tcpPort}");

        // 5. abrir TCP al destino indicado
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(tcpHost, tcpPort);
        Console.WriteLine($"pac-mock lims: TCP connected");
        await using var stream = tcp.GetStream();

        // 6. enviar JSON + NUL
        await stream.WriteAsync(payload);
        await stream.WriteAsync(new byte[] { 0x00 });
        await stream.FlushAsync();
        tcp.Client.Shutdown(SocketShutdown.Send);
        Console.WriteLine($"pac-mock lims: sent {payload.Length}B JSON + NUL");

        // 7. leer respuesta JSON
        var responseBuffer = new MemoryStream();
        var buf = new byte[4096];
        using var readCts = new CancellationTokenSource(opts.TimeoutMs);
        try
        {
            while (true)
            {
                var n = await stream.ReadAsync(buf, readCts.Token);
                if (n == 0) break;
                responseBuffer.Write(buf, 0, n);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"pac-mock lims: timeout reading response");
        }

        var responseText = Encoding.UTF8.GetString(responseBuffer.ToArray());
        Console.WriteLine($"pac-mock lims: server response ({responseBuffer.Length}B): {responseText}");

        // 8. exito si la respuesta no contiene "NACK"
        var ok = !responseText.Contains("NACK", StringComparison.OrdinalIgnoreCase);
        return ok ? 0 : 5;
    }

    private static async Task<IPAddress> ResolveTargetAsync(string target)
    {
        if (IPAddress.TryParse(target, out var ip)) return ip;
        var addrs = await Dns.GetHostAddressesAsync(target);
        var v4 = Array.Find(addrs, a => a.AddressFamily == AddressFamily.InterNetwork);
        return v4 ?? addrs.First();
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
        Console.WriteLine("pac-mock lims send — simulate full LIMS Ethernet handshake from the equipment side");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("    pac-mock lims send --target HOST --json FILE [--udp-port 3000] [--timeout-ms 5000]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("    --target HOST       Where to send the UDP beacon (typically 127.0.0.1)");
        Console.WriteLine("    --json FILE         Path to JSON sample file (DataDictionary format)");
        Console.WriteLine("    --udp-port PORT     UDP destination port for beacon (default 3000)");
        Console.WriteLine("    --timeout-ms MS     Timeout for ACK + response (default 5000)");
        Console.WriteLine();
        Console.WriteLine("EXIT CODES:");
        Console.WriteLine("    0   success");
        Console.WriteLine("    3   timeout waiting for ACK");
        Console.WriteLine("    4   malformed ACK from server");
        Console.WriteLine("    5   server response contained NACK");
        Console.WriteLine("    64  invalid command-line arguments");
    }
}

internal sealed record LimsSendOptions(string Target, string JsonPath, int UdpPort, int TimeoutMs);
