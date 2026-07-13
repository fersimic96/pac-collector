using System.Net;
using System.Net.Sockets;
using System.Text;
using PacCollector.Application.UseCases;

namespace PacCollector.Infrastructure.Network;

// servidor TCP :631 (puerto IPP estandar). Acepta dos protocolos:
//   - HTTP/IPP: PCs Windows con CUPS lo usan via "Generic / MS Publisher Color Printer"
//   - Raw PCL/HP-GL: equipos PAC viejos (Iris) mandan el job sin envoltura HTTP
// se clasifica con los primeros bytes (sniff de hasta 32) y se rutea.
public sealed class PrintServer
{
    private const int HeadersMax = 65_536;
    private const int ReadTimeoutMs = 15_000;
    private const int IdleGapMs = 800;
    private const int RawMaxBytes = 4 * 1024 * 1024;
    private const int DetectMinBytes = 32;
    private const int DetectPerReadMs = 300;
    private const int MaxDetectReads = 8;
    private static readonly byte[] Uel = { 0x1B, (byte)'%', (byte)'-', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'X' };
    private static readonly byte[] HeaderTerminator = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

    private readonly IPEndPoint _bindAddr;
    private readonly ReceivePrintUseCase _receivePrint;
    private readonly Action<string>? _log;

    public PrintServer(
        IPEndPoint bindAddr,
        ReceivePrintUseCase receivePrint,
        Action<string>? log = null)
    {
        _bindAddr = bindAddr;
        _receivePrint = receivePrint;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_bindAddr);
        listener.Start();
        _log?.Invoke($"Print server listening on {_bindAddr} (IPP + raw serial-over-TCP)");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                _ = HandleConnectionAsync(client, ct);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint as IPEndPoint;
        var remoteIp = remote?.Address.ToString();
        _log?.Invoke($">>> Print connection from {remote}");

        try
        {
            using (client)
            {
                using var stream = client.GetStream();
                var sniff = await SniffAsync(stream, ct).ConfigureAwait(false);
                if (sniff.Length == 0)
                {
                    _log?.Invoke($"Print {remote}: connection closed without data");
                    return;
                }

                switch (PrintClassifier.Classify(sniff))
                {
                    case PrintClassification.Http:
                        await HandleIppAsync(stream, remoteIp, sniff, ct).ConfigureAwait(false);
                        break;
                    case PrintClassification.Raw:
                    case PrintClassification.Indeterminate:
                    default:
                        await HandleRawAsync(stream, remoteIp, sniff, ct).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log?.Invoke($"Print TCP {remote}: {e.Message}");
        }
    }

    // lee hasta DetectMinBytes o hasta que se decida la clasificacion. Max 8 reads cortos.
    private static async Task<byte[]> SniffAsync(NetworkStream stream, CancellationToken ct)
    {
        var sniff = new List<byte>(64);
        var chunk = new byte[64];
        for (var i = 0; i < MaxDetectReads; i++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DetectPerReadMs);
            int n;
            try { n = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            if (n == 0) break;
            sniff.AddRange(chunk[..n]);
            if (sniff.Count >= DetectMinBytes) break;
            if (PrintClassifier.Classify(sniff.ToArray()) != PrintClassification.Indeterminate) break;
        }
        return sniff.ToArray();
    }

    // raw: acumula bytes hasta encontrar 2 UEL markers (end-of-job) o idle gap o max-bytes
    private async Task HandleRawAsync(NetworkStream stream, string? remoteIp, byte[] head, CancellationToken ct)
    {
        var buf = new List<byte>(8192);
        buf.AddRange(head);
        var chunk = new byte[16_384];
        var uelCount = CountSubseq(buf, Uel);

        while (true)
        {
            if (buf.Count >= RawMaxBytes)
            {
                _log?.Invoke($"Print raw {remoteIp}: hit RawMaxBytes, force-closing");
                break;
            }
            if (uelCount >= 2) break;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IdleGapMs);
            int n;
            try { n = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            if (n == 0) break;

            // scan_start: arrancar un poco antes para no perder UEL que cae sobre el limite
            var scanStart = Math.Max(0, buf.Count - Uel.Length);
            buf.AddRange(chunk[..n]);
            uelCount += CountSubseq(buf.GetRange(scanStart, buf.Count - scanStart), Uel);
            if (uelCount >= 2) break;
        }

        try { stream.Socket.Shutdown(SocketShutdown.Both); } catch { /* best-effort */ }

        if (buf.Count > 0)
        {
            _log?.Invoke($"Print raw {remoteIp}: end-of-job ({buf.Count} bytes, uelCount={uelCount})");
            try { await _receivePrint.ExecuteAsync(buf.ToArray(), remoteIp, ct).ConfigureAwait(false); }
            catch (Exception e) when (e is not OutOfMemoryException and not OperationCanceledException)
            {
                _log?.Invoke($"Print raw {remoteIp}: process error: {e.Message}");
            }
        }
    }

    // IPP: lee headers HTTP + body por Content-Length, responde IPP OK y procesa el body
    private async Task HandleIppAsync(NetworkStream stream, string? remoteIp, byte[] head, CancellationToken ct)
    {
        _log?.Invoke($">>> Print/IPP connection from {remoteIp}");
        var buf = new List<byte>(8192);
        buf.AddRange(head);
        var chunk = new byte[16_384];

        var headersEnd = IndexOfSubseq(buf, HeaderTerminator);
        if (headersEnd >= 0) headersEnd += HeaderTerminator.Length;

        while (headersEnd < 0 && buf.Count < HeadersMax)
        {
            var n = await ReadWithTimeoutAsync(stream, chunk, ReadTimeoutMs, ct).ConfigureAwait(false);
            if (n <= 0) break;
            buf.AddRange(chunk[..n]);
            headersEnd = IndexOfSubseq(buf, HeaderTerminator);
            if (headersEnd >= 0) headersEnd += HeaderTerminator.Length;
        }

        if (headersEnd < 0)
        {
            _log?.Invoke($"Print {remoteIp}: no HTTP headers terminator received");
            return;
        }

        var headerText = Encoding.ASCII.GetString(buf.GetRange(0, headersEnd - HeaderTerminator.Length).ToArray());
        var contentLength = ParseContentLength(headerText);

        // leer el cuerpo segun Content-Length
        while (buf.Count < headersEnd + contentLength)
        {
            var need = headersEnd + contentLength - buf.Count;
            var want = Math.Min(need, chunk.Length);
            var n = await ReadWithTimeoutAsync(stream, chunk.AsMemory(0, want), ReadTimeoutMs, ct).ConfigureAwait(false);
            if (n <= 0) break;
            buf.AddRange(chunk[..n]);
        }

        // request_id IPP: bytes 4..7 del cuerpo IPP
        ReadOnlySpan<byte> requestId;
        if (buf.Count >= headersEnd + 8)
        {
            requestId = new[] { buf[headersEnd + 4], buf[headersEnd + 5], buf[headersEnd + 6], buf[headersEnd + 7] };
        }
        else
        {
            requestId = new byte[] { 0, 0, 0, 1 };
        }

        var response = IppResponseBuilder.BuildOk(requestId);
        try { await stream.WriteAsync(response, ct).ConfigureAwait(false); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log?.Invoke($"Print {remoteIp}: write response: {e.Message}");
        }
        try { stream.Socket.Shutdown(SocketShutdown.Both); } catch { /* best-effort */ }

        // procesar el cuerpo IPP (despues de los headers HTTP) como print payload
        if (buf.Count > headersEnd)
        {
            var body = buf.GetRange(headersEnd, buf.Count - headersEnd).ToArray();
            if (body.Length > 0)
            {
                try { await _receivePrint.ExecuteAsync(body, remoteIp, ct).ConfigureAwait(false); }
                catch (Exception e) when (e is not OutOfMemoryException and not OperationCanceledException)
                {
                    _log?.Invoke($"Print {remoteIp}: process error: {e.Message}");
                }
            }
        }
    }

    private static async Task<int> ReadWithTimeoutAsync(NetworkStream stream, Memory<byte> buf, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try { return await stream.ReadAsync(buf, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return 0; }
    }

    private static int ParseContentLength(string headers)
    {
        foreach (var line in headers.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var v = trimmed["Content-Length:".Length..].Trim();
                if (int.TryParse(v, out var n)) return n;
            }
        }
        return 0;
    }

    internal static int IndexOfSubseq(List<byte> haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Count < needle.Length) return -1;
        for (var i = 0; i <= haystack.Count - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    internal static int CountSubseq(List<byte> haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Count < needle.Length) return 0;
        var count = 0;
        var i = 0;
        while (i + needle.Length <= haystack.Count)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) { count++; i += needle.Length; }
            else i++;
        }
        return count;
    }
}
