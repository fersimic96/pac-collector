// ══════════════════════════════════════════════════════════════════════════════
// DOMAIN EVENTS — el "vocabulario" de cosas que le pueden pasar al sistema.
// Cada vez que algo relevante ocurre (llegó una muestra, se descubrió un equipo,
// falló guardar), el pipeline PUBLICA uno de estos eventos al EventBus.
// Después el WebSocket los toma y los manda al frontend en vivo.
//
// Viven en Domain porque son parte del NEGOCIO — describen qué pasa en el dominio
// del laboratorio, sin saber cómo se transmiten (WebSocket, log, etc.).
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
namespace PacCollector.Domain.Ports;   // namespace [C#] — la "dirección" de este archivo

// record [C#] = una clase pensada para GUARDAR DATOS que no cambian (inmutable).
//   Un "record" te regala gratis: comparación por valor, ToString() legible, y
//   la sintaxis corta de abajo (los datos van entre paréntesis, sin escribir el cuerpo).
// abstract [C#] = "no se puede crear un DomainEvent 'pelado'; solo sus variantes de abajo".
//   Funciona como una categoría padre: todos los de abajo SON un DomainEvent.
public abstract record DomainEvent
{
    // Cada línea de abajo es UNA variante de evento. Se leen así:
    //   sealed [C#]   = "nadie puede heredar de este" (es una hoja final)
    //   record [C#]   = guarda datos inmutables
    //   Nombre(...)   = los datos que lleva el evento, entre paréntesis
    //   : DomainEvent = "este evento ES un DomainEvent" (hereda de la categoría padre)

    // llegó el beacon UDP de un equipo. Lleva: la IP y el momento (Ts = timestamp).
    public sealed record BeaconReceived(string Ip, DateTimeOffset Ts) : DomainEvent;
    // └─ string [C#] = texto  .  DateTimeOffset [.NET] = fecha+hora con zona horaria

    // se detectó un equipo que nunca habíamos visto. El "?" en string? = "puede ser null".
    public sealed record InstrumentDiscovered(
        string Serial,          // número de serie del equipo
        string AnalyzerType,    // tipo: OptiPMD, OptiDist, etc.
        string? Ip) : DomainEvent;   // string? [C#] = la IP puede faltar (null)

    // un equipo ya conocido volvió a comunicarse (actualiza su última IP/hora).
    public sealed record InstrumentTouched(
        string Serial,
        string? Ip) : DomainEvent;

    // ¡EL IMPORTANTE! se recibió y guardó una muestra con éxito.
    // El frontend escucha este para mostrar la muestra nueva en pantalla.
    public sealed record SampleReceived(
        string Uuid,               // id único de la muestra
        string Serial,             // qué equipo la mandó
        string SampleIdentifier,   // id de la muestra según el equipo
        double? Ibp,               // double? [C#] = número decimal que puede ser null. Ibp = punto inicial de ebullición
        double? Fbp) : DomainEvent;// Fbp = punto final de ebullición

    // llegó una muestra DUPLICADA (misma muestra que ya teníamos) → se ignoró.
    public sealed record SampleDuplicateSkipped(
        string Serial,
        string SampleIdentifier) : DomainEvent;

    // el plugin no pudo parsear el mensaje del equipo.
    public sealed record PluginParseFailed(
        string AnalyzerType,
        string Reason) : DomainEvent;   // Reason = por qué falló

    // llegó algo que NO reconocemos (equipo sin plugin) → se guardó en cuarentena.
    public sealed record UnknownPayloadReceived(
        string? AnalyzerType,
        string? SourceIp,
        ulong Bytes,          // ulong [C#] = número entero grande sin signo. Cuántos bytes tenía
        string Reason,
        string SavedPath) : DomainEvent;   // dónde se guardó el payload sospechoso

    // falló guardar la muestra (disco lleno, permisos, etc.).
    public sealed record PersistenceFailed(
        string Stage,              // en qué paso falló (save_sample, write_artifacts...)
        string? Serial,
        string? SampleIdentifier,
        string Reason) : DomainEvent;

    // error genérico del servidor.
    public sealed record ServerError(string Message) : DomainEvent;
}
// ─── ¿Por qué todos anidados adentro de DomainEvent? ──────────────────────────
// Para poder escribir "DomainEvent.SampleReceived", "DomainEvent.BeaconReceived", etc.
// Quedan agrupados bajo un mismo "apellido" y el compilador puede chequear que
// cuando manejás eventos, cubriste todas las variantes (pattern matching exhaustivo).
