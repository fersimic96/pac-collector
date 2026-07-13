// ══════════════════════════════════════════════════════════════════════════════
// UdpServer — el "¿HAY ALGUIEN AHÍ?" del protocolo.
// UDP es como GRITAR en una sala: mensajes sueltos, sin conexión, sin garantías.
// El equipo PAC grita su beacon [0x01 0x02 0x03] cada ~2 segundos a la red (:3000).
// Nosotros escuchamos y le respondemos "ACK <mi_ip> <mi_puerto_tcp>", que le dice al
// equipo: "te escuché, ahora mandame los datos por TCP a esta dirección".
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using System.Net;           // [.NET] IPEndPoint, IPAddress (direcciones de red)
using System.Net.Sockets;   // [.NET] UdpClient, Socket (los "enchufes" de red)
using PacCollector.Application.UseCases;   // [NUESTRO] HandleBeaconUseCase

namespace PacCollector.Infrastructure.Network;

public sealed class UdpServer
{
    // ─── DEPENDENCIAS ─────────────────────────────────────────────────────────
    private readonly IPEndPoint _bindAddr;         // [.NET] en qué IP:puerto escuchar (ej 0.0.0.0:3000)
    private readonly string? _configuredIp;        // IP a poner en el ACK (o null = detectar sola)
    private readonly ushort _tcpPort;              // ushort [C#] = entero chico. El puerto TCP a anunciar (9980)
    private readonly HandleBeaconUseCase _handleBeacon;  // [NUESTRO] el use case que avisa "equipo detectado"
    private readonly Action<string>? _log;         // Action<string> [.NET] = "una función que recibe un texto" (para loguear)

    public UdpServer(
        IPEndPoint bindAddr,
        string? configuredIp,
        ushort tcpPort,
        HandleBeaconUseCase handleBeacon,
        Action<string>? log = null)                // = null [C#] = parámetro opcional
    {
        _bindAddr = bindAddr;
        _configuredIp = configuredIp;
        _tcpPort = tcpPort;
        _handleBeacon = handleBeacon;
        _log = log;
    }

    // ─── EL LOOP DE ESCUCHA ───────────────────────────────────────────────────
    // Corre en segundo plano para siempre (hasta que se pida cancelar).
    public async Task RunAsync(CancellationToken ct)
    {
        using var udp = new UdpClient(_bindAddr);            // UdpClient [.NET] = el "enchufe" UDP, escuchando en _bindAddr
        try { udp.EnableBroadcast = true; } catch { /* best-effort */ }   // permitir mensajes broadcast
        _log?.Invoke($"UDP listening on {_bindAddr}");        // _log?.Invoke [C#] = "si _log no es null, llamalo"

        while (!ct.IsCancellationRequested)                  // while [C#] — repetir hasta que pidan cancelar
        {
            UdpReceiveResult result;                         // UdpReceiveResult [.NET] = lo que llegó + de quién
            try
            {
                result = await udp.ReceiveAsync(ct).ConfigureAwait(false);   // ReceiveAsync [.NET] = ESPERA un mensaje UDP
                // └─ ConfigureAwait(false) [.NET] = detalle técnico de performance, ignoralo por ahora
            }
            catch (OperationCanceledException) { break; }    // pidieron apagar → salir del loop
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _log?.Invoke($"UDP recv error: {e.Message}");
                continue;                                    // continue [C#] = saltar al próximo ciclo del while
            }

            var data = result.Buffer;                        // los bytes que llegaron
            var remote = result.RemoteEndPoint;              // quién los mandó (IP:puerto del equipo)

            if (IsBeacon(data))                              // ¿es el beacon [0x01 0x02 0x03]?
            {
                // averiguar qué IP anunciarle al equipo
                var localIp = _configuredIp                              // 1° la IP configurada a mano, si hay
                    ?? LocalIpForRemote(remote)?.ToString()              // 2° o la IP local que "ve" el equipo
                    ?? "127.0.0.1";                                      // 3° o loopback como último recurso
                var ack = Protocol.BuildAck(localIp, _tcpPort);          // Protocol.BuildAck [NUESTRO] = arma "ACK <ip> <port>"
                try
                {
                    var ackBytes = System.Text.Encoding.ASCII.GetBytes(ack);   // texto → bytes ASCII
                    await udp.SendAsync(ackBytes, remote, ct).ConfigureAwait(false);   // SendAsync [.NET] = responder al equipo
                    _log?.Invoke($"ACK to {remote} → IP={localIp}");
                    _handleBeacon.Execute(remote.Address.ToString());     // [NUESTRO] avisar al frontend "equipo detectado"
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    _log?.Invoke($"UDP send ACK to {remote}: {e.Message}");
                }
            }
            else                                             // llegó algo que NO es el beacon
            {
                _log?.Invoke($"UDP non-beacon from {remote} ({data.Length}B)");
                try { await udp.SendAsync(Protocol.Nak, remote, ct).ConfigureAwait(false); }  // responder NAK (rechazo)
                catch { /* best-effort NAK */ }
            }
        }
    }

    // ─── ¿los bytes son exactamente el beacon? ────────────────────────────────
    private static bool IsBeacon(ReadOnlySpan<byte> data)
        => data.Length == Protocol.Beacon.Length && data.SequenceEqual(Protocol.Beacon);
    // └─ mismo largo Y misma secuencia de bytes que Protocol.Beacon [NUESTRO] (= [0x01,0x02,0x03])

    // ─── truco para saber "con qué IP me ve el equipo" ───────────────────────
    // Una PC puede tener varias IPs (WiFi, cable, VPN). Este truco abre un socket
    // "conectado" al equipo SIN mandar nada, y le pregunta al sistema operativo qué
    // IP local usaría para llegar a él. Esa es la IP correcta para anunciar en el ACK.
    private static IPAddress? LocalIpForRemote(IPEndPoint remote)
    {
        try
        {
            using var probe = new Socket(remote.AddressFamily, SocketType.Dgram, ProtocolType.Udp);  // Socket [.NET]
            probe.Connect(remote);                                   // no manda datos, solo "apunta"
            return (probe.LocalEndPoint as IPEndPoint)?.Address;     // la IP local que el SO eligió
        }
        catch
        {
            return null;
        }
    }
}
