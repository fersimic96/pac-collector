// ══════════════════════════════════════════════════════════════════════════════
// USE CASE HandleBeaconUseCase — el caso de uso MÁS SIMPLE de toda la app.
// Cuando un equipo manda su beacon UDP (su "hola, estoy acá"), este caso de uso
// solo publica un evento para que el frontend muestre "equipo detectado".
//
// Sirve para ver el ESQUELETO de un use case sin ruido: recibe una dependencia por
// el constructor, la guarda, y en Execute hace su única tarea.
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using PacCollector.Domain.Ports;   // [NUESTRO] trae IEventBus y DomainEvent

namespace PacCollector.Application.UseCases;

public sealed class HandleBeaconUseCase
{
    // ─── DEPENDENCIA ──────────────────────────────────────────────────────────
    // readonly [C#] = se asigna una vez (en el constructor) y no cambia más.
    // IEventBus [NUESTRO] = la INTERFAZ del bus de eventos. Fijate: pide la interfaz,
    // no la clase concreta ChannelEventBus. El use case no sabe cómo se transmiten
    // los eventos, solo que "hay algo donde publicar".
    private readonly IEventBus _events;

    // ─── CONSTRUCTOR ──────────────────────────────────────────────────────────
    // Acá es donde entra la "inyección de dependencias": el DI container le PASA
    // el IEventBus ya creado. El use case no hace "new ChannelEventBus()" — lo recibe.
    public HandleBeaconUseCase(IEventBus events) => _events = events;
    // └─ recibe "events" y lo guarda en "_events". El => es la forma corta de un cuerpo de 1 línea.

    // ─── LA ÚNICA TAREA ───────────────────────────────────────────────────────
    // void [C#] = no devuelve nada. Recibe la IP del equipo que mandó el beacon.
    public void Execute(string ip)
        => _events.Publish(new DomainEvent.BeaconReceived(ip, DateTimeOffset.UtcNow));
    // └─ _events.Publish(...) [NUESTRO] = "avisá a todos que pasó esto"
    //    new DomainEvent.BeaconReceived(...) [NUESTRO] = crea el evento (uno de los 9 que vimos)
    //    DateTimeOffset.UtcNow [.NET] = el momento exacto, en UTC (nunca hora local)
}
