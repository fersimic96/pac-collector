// ══════════════════════════════════════════════════════════════════════════════
// ★ ListenerManager — EL ORQUESTADOR DE LA RED ★
// ¿Quién crea y arranca el UdpServer y el TcpServer? ESTE.
//
// CADENA COMPLETA:
//   Program.cs  →  ListenerManager.StartLims()  →  crea UdpServer + TcpServer  →  los corre en background
//
// Además permite ARRANCAR y PARAR los servidores en caliente desde la UI
// (endpoints /api/listeners), sin reiniciar la app. Maneja los 3 servidores:
// UDP (:3000), TCP (:9980) y Print (:631).
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using System.Net;                          // [.NET] IPEndPoint, IPAddress
using PacCollector.Application.UseCases;   // [NUESTRO] los use cases que le pasa a cada servidor
using PacCollector.Infrastructure.Config;  // [NUESTRO] ConfigStore

namespace PacCollector.Infrastructure.Network;

public sealed class ListenerManager
{
    // ─── DEPENDENCIAS (se las inyecta el DI, ver Program.cs PASO 5) ───────────
    // Fijate: guarda los USE CASES, no los servidores. Los servidores los crea
    // cada vez que arranca, y les pasa estos use cases.
    private readonly ConfigStore _config;                // [NUESTRO] para leer qué IP/puertos usar
    private readonly HandleBeaconUseCase _handleBeacon;  // [NUESTRO] se lo pasa al UdpServer
    private readonly ReceiveSampleUseCase _receiveSample;// [NUESTRO] se lo pasa al TcpServer
    private readonly ReceivePrintUseCase _receivePrint;  // [NUESTRO] se lo pasa al PrintServer
    private readonly Action<string>? _log;               // [.NET] para loguear

    // ─── ESTADO: los "controles remotos" de cada servidor corriendo ───────────
    // CancellationTokenSource [.NET] = el "botón de apagado" de cada grupo.
    private CancellationTokenSource? _limsCts;   // apaga UDP+TCP
    private CancellationTokenSource? _printCts;  // apaga Print
    // Task? [.NET] = la referencia al servidor corriendo en background (o null si está apagado).
    private Task? _udpTask;
    private Task? _tcpTask;
    private Task? _printTask;
    private readonly Lock _gate = new();         // Lock [.NET] = candado, para que start/stop no choquen entre hilos

    public ListenerManager(
        ConfigStore config,
        HandleBeaconUseCase handleBeacon,
        ReceiveSampleUseCase receiveSample,
        ReceivePrintUseCase receivePrint,
        Action<string>? log = null)
    {
        _config = config;
        _handleBeacon = handleBeacon;
        _receiveSample = receiveSample;
        _receivePrint = receivePrint;
        _log = log;
    }

    // los puertos fijos del protocolo (const [C#] = valores que nunca cambian)
    public const ushort UdpPort = 3000;    // el equipo grita su beacon acá
    public const ushort TcpPort = 9980;    // el equipo manda la muestra acá

    // ─── PROPIEDADES DE ESTADO (las lee la UI para mostrar "corriendo/detenido") ─
    // bool [C#]. "lock (_gate)" = leer el estado sin que un start/stop lo cambie a la mitad.
    public bool LimsRunning
    {
        get { lock (_gate) return _udpTask is not null || _tcpTask is not null; }
    }

    public bool PrintRunning
    {
        get { lock (_gate) return _printTask is not null; }
    }

    // ══ ACÁ NACE EL UdpServer (y el TcpServer) ════════════════════════════════
    // Lo llama Program.cs al arrancar (si AutoStartServer), o la UI vía /api/listeners.
    public void StartLims()
    {
        lock (_gate)                                   // candado: que no arranquen dos veces a la vez
        {
            if (_udpTask is not null || _tcpTask is not null) return;   // ya están corriendo → no hacer nada
            _limsCts = new CancellationTokenSource();   // crear el "botón de apagado" para este grupo

            var cfg = _config.Snapshot();               // leer la config actual (qué IP anunciar)
            var udpBind = new IPEndPoint(IPAddress.Any, UdpPort);   // IPAddress.Any [.NET] = escuchar en TODAS las IPs de la PC, puerto 3000
            var tcpBind = new IPEndPoint(IPAddress.Any, TcpPort);   // ídem, puerto 9980

            // ── CREAR LOS SERVIDORES (acá se instancian las clases que comentaste) ──
            var udp = new UdpServer(udpBind, cfg.General.SelectedIp, TcpPort, _handleBeacon, _log);
            // └─ le pasa: dónde escuchar, qué IP anunciar, el puerto TCP, y el use case del beacon
            var tcp = new TcpServer(tcpBind, _receiveSample, _log);
            // └─ le pasa: dónde escuchar y el use case que procesa la muestra

            // ── ARRANCARLOS EN BACKGROUND ──
            _udpTask = Task.Run(() => udp.RunAsync(_limsCts.Token));   // Task.Run [.NET] = "corré esto en otro hilo, no me bloquees"
            _tcpTask = Task.Run(() => tcp.RunAsync(_limsCts.Token));   // ambos loops corren en paralelo, para siempre
            // └─ _limsCts.Token = la señal de cancelación. Cuando StopLims() apriete el botón, estos loops terminan.
        }
    }

    public void StopLims()
    {
        Task? udp, tcp;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _limsCts;
            udp = _udpTask;
            tcp = _tcpTask;
            _limsCts = null;
            _udpTask = null;
            _tcpTask = null;
        }
        cts?.Cancel();
        try { udp?.GetAwaiter().GetResult(); } catch { /* ignore */ }
        try { tcp?.GetAwaiter().GetResult(); } catch { /* ignore */ }
        cts?.Dispose();
    }

    public void StartPrint()
    {
        lock (_gate)
        {
            if (_printTask is not null) return;
            _printCts = new CancellationTokenSource();
            var cfg = _config.Snapshot();
            var bind = new IPEndPoint(IPAddress.Any, cfg.General.PrintPort);
            var print = new PrintServer(bind, _receivePrint, _log);
            _printTask = Task.Run(() => print.RunAsync(_printCts.Token));
        }
    }

    public void StopPrint()
    {
        Task? print;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _printCts;
            print = _printTask;
            _printCts = null;
            _printTask = null;
        }
        cts?.Cancel();
        try { print?.GetAwaiter().GetResult(); } catch { /* ignore */ }
        cts?.Dispose();
    }

    public void StopAll()
    {
        StopLims();
        StopPrint();
    }
}
