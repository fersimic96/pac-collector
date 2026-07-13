// ══════════════════════════════════════════════════════════════════════════════
// TcpServer — el "ENVÍO FORMAL DE DATOS".
// TCP es como una LLAMADA TELEFÓNICA: se establece una conexión firme, los datos
// llegan en orden y completos. Después del ACK por UDP, el equipo abre una conexión
// TCP a :9980 y manda:  { JSON de la muestra } + un byte 0x00 (NUL) que marca el final.
// Nosotros leemos, procesamos (llamando al pipeline) y respondemos el checksum.
//
// ★ ACÁ ES DONDE LA RED SE CONECTA CON LA APLICACIÓN: _receiveSample.ExecuteAsync ★
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using System.Net;           // [.NET] IPEndPoint
using System.Net.Sockets;   // [.NET] TcpListener, TcpClient, NetworkStream
using System.Text;          // [.NET] Encoding
using PacCollector.Application.UseCases;   // [NUESTRO] ReceiveSampleUseCase
using PacCollector.Domain.ValueObjects;    // [NUESTRO] PacChecksum

namespace PacCollector.Infrastructure.Network;

public sealed class TcpServer
{
    private readonly IPEndPoint _bindAddr;                 // [.NET] dónde escuchar (ej 0.0.0.0:9980)
    private readonly ReceiveSampleUseCase _receiveSample;  // [NUESTRO] el use case que arranca el pipeline
    private readonly Action<string>? _log;                 // [.NET] para loguear

    public TcpServer(
        IPEndPoint bindAddr,
        ReceiveSampleUseCase receiveSample,
        Action<string>? log = null)
    {
        _bindAddr = bindAddr;
        _receiveSample = receiveSample;
        _log = log;
    }

    // ─── EL LOOP QUE ACEPTA CONEXIONES ────────────────────────────────────────
    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(_bindAddr);          // TcpListener [.NET] = "el que atiende el teléfono"
        listener.Start();                                   // empezar a aceptar llamadas
        _log?.Invoke($"TCP listening on {_bindAddr}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;                           // TcpClient [.NET] = una conexión con un equipo
                try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                // └─ AcceptTcpClientAsync [.NET] = ESPERA a que un equipo se conecte
                catch (OperationCanceledException) { break; }
                _ = HandleConnectionAsync(client, ct);
                // └─ "_ =" [C#] = FIRE-AND-FORGET: atender esta conexión SIN esperarla, para poder
                //    aceptar otras al mismo tiempo. Varios equipos pueden mandar a la vez.
                //    Es seguro porque HandleConnectionAsync tiene su propio try/catch adentro.
            }
        }
        finally
        {
            listener.Stop();                                // finally [C#] = pase lo que pase, cerrar el listener
        }
    }

    // ─── ATENDER UNA CONEXIÓN (una muestra) ───────────────────────────────────
    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint as IPEndPoint;   // quién se conectó
        var remoteIp = remote?.Address.ToString();                 // su IP como texto
        _log?.Invoke($">>> TCP CONNECTED from {remote}");

        try
        {
            using (client)                                         // using [C#] = cerrar la conexión al terminar
            {
                using var stream = client.GetStream();             // NetworkStream [.NET] = el "caño" por donde fluyen los bytes
                var buf = await ReadUntilNullAsync(stream, ct).ConfigureAwait(false);
                // └─ leer todos los bytes hasta el 0x00 (la muestra completa). Método abajo.

                TcpLimsResponse response;                          // [NUESTRO] la respuesta a devolver
                try
                {
                    // ★★ ACÁ LA RED LLAMA A LA APLICACIÓN ★★
                    var checksum = await _receiveSample.ExecuteAsync(buf, remoteIp, ct).ConfigureAwait(false);
                    // └─ ExecuteAsync [NUESTRO] = corre TODO el pipeline (parsear, guardar, evento)
                    //    y devuelve el checksum que el equipo espera.
                    response = TcpLimsResponse.Ok(checksum.AsString);   // respuesta OK con el checksum
                }
                catch (Exception e) when (e is not OutOfMemoryException and not OperationCanceledException)
                {
                    _log?.Invoke($"TCP from {remote}: process error: {e.Message}");
                    // aunque el procesamiento falle, SIEMPRE respondemos el checksum del mensaje crudo
                    var checksum = PacChecksum.FromBytes(buf.Span);
                    response = TcpLimsResponse.Nack(checksum.AsString);  // respuesta con NACK (hubo un problema)
                }

                var json = response.ToCompactJson();               // [NUESTRO] la respuesta como JSON compacto
                var bytes = Encoding.UTF8.GetBytes(json);          // texto → bytes
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);   // WriteAsync [.NET] = mandar la respuesta al equipo
                try { client.Client.Shutdown(SocketShutdown.Both); } catch { /* best-effort */ }  // cerrar prolijo
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log?.Invoke($"TCP {remote}: {e.Message}");            // el try/catch que hace seguro al fire-and-forget
        }
    }

    // ─── LEER BYTES HASTA EL NUL (0x00) ───────────────────────────────────────
    // El equipo no dice "mi mensaje mide X bytes". Manda datos y al final un byte 0x00.
    // Entonces leemos de a pedazos ("chunks") hasta encontrar ese 0x00.
    private static async Task<ReadOnlyMemory<byte>> ReadUntilNullAsync(
        NetworkStream stream,
        CancellationToken ct)
    {
        var buf = new List<byte>(8192);                    // List<byte> [.NET] = acá vamos acumulando el mensaje
        var chunk = new byte[Protocol.TcpReadChunk];       // buffer temporal para cada lectura

        while (true)                                       // repetir hasta encontrar el 0x00 o que corten
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);   // [.NET] timeout por lectura
            cts.CancelAfter(Protocol.TcpReadTimeoutMs);    // [NUESTRO] si tarda mucho (5s), cortar — evita colgarse
            int read;
            try
            {
                read = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false);   // ReadAsync [.NET] = leer un pedazo
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;                                     // timeout de lectura → dejar de leer
            }
            if (read == 0) break;                          // el equipo cerró la conexión → terminamos

            var idx = Array.IndexOf(chunk, Protocol.NullTerminator, 0, read);   // ¿está el 0x00 en este pedazo?
            if (idx >= 0)
            {
                buf.AddRange(chunk[..idx]);                // agregar hasta ANTES del 0x00 (sin incluirlo)
                break;                                     // encontramos el final → salir
            }
            buf.AddRange(chunk[..read]);                   // no estaba el 0x00 → guardar todo y seguir leyendo
        }

        return buf.ToArray();                              // devolver el mensaje completo como bytes
    }
}
