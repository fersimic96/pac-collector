// ══════════════════════════════════════════════════════════════════════════════
// ★★ SampleProcessingService — EL CORAZÓN DEL SISTEMA ★★
// Acá se juntan TODAS las piezas. Es el "pipeline" que convierte un mensaje crudo
// del equipo en una muestra guardada. Fijate que NO importa nada de red ni de disco
// directamente: trabaja SOLO con interfaces del Domain. No sabe si los datos vienen
// por TCP o si se guardan en JSON o SQLite — solo orquesta.
//
// El pipeline (ProcessRawMessageAsync) hace 6 pasos:
//   1. decodificar bytes → texto → JSON, sacar el AnalyzerType
//   2. buscar el plugin de ese tipo de equipo (si no hay → cuarentena y salir)
//   3. el plugin parsea el mensaje → objeto Sample
//   4. upsert del instrumento (nuevo → evento "descubierto")
//   5. deduplicación (¿ya teníamos esta muestra? → salir)
//   6. persistir en 3 pasos + emitir evento "SampleReceived"
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using System.Text;          // [.NET] Encoding (bytes ↔ texto), StringBuilder
using System.Text.Json;     // [.NET] JsonDocument (parsear JSON)
using PacCollector.Domain.Entities;      // [NUESTRO] Sample, Instrument
using PacCollector.Domain.Errors;        // [NUESTRO] DomainException
using PacCollector.Domain.Ports;         // [NUESTRO] las interfaces + DomainEvent
using PacCollector.Domain.ValueObjects;  // [NUESTRO] AnalyzerSerial

namespace PacCollector.Application.Services;

public sealed class SampleProcessingService
{
    // ─── LAS 5 DEPENDENCIAS (todas son INTERFACES del Domain) ─────────────────
    // Este service no crea ninguna de estas: se las inyecta el DI container.
    // Y como son interfaces, no sabe qué implementación real hay detrás.
    private readonly IPluginRegistry _plugins;         // [NUESTRO] dónde buscar el parser del equipo
    private readonly ISampleRepository _samples;       // [NUESTRO] dónde guardar/consultar muestras
    private readonly IInstrumentRepository _instruments;// [NUESTRO] dónde guardar/consultar equipos
    private readonly IFileWriter _files;               // [NUESTRO] cómo escribir los archivos de salida
    private readonly IEventBus _events;                // [NUESTRO] dónde publicar los eventos

    // ─── CONSTRUCTOR ──────────────────────────────────────────────────────────
    // Recibe las 5 dependencias y las guarda. El DI container arma esta lista solo,
    // porque todas fueron registradas en Program.cs (PASO 5).
    public SampleProcessingService(
        IPluginRegistry plugins,
        ISampleRepository samples,
        IInstrumentRepository instruments,
        IFileWriter files,
        IEventBus events)
    {
        _plugins = plugins;
        _samples = samples;
        _instruments = instruments;
        _files = files;
        _events = events;
    }

    // ══ EL PIPELINE PRINCIPAL (muestras que llegan por TCP LIMS) ══════════════
    // Task<bool> [.NET] = devuelve true si guardó la muestra, false si la descartó.
    public async Task<bool> ProcessRawMessageAsync(
        ReadOnlyMemory<byte> raw,          // los bytes crudos del equipo
        string? sourceIp,                  // de qué IP vinieron
        CancellationToken ct = default)    // señal de cancelación
    {
        var now = DateTimeOffset.UtcNow;   // [.NET] el momento de recepción, en UTC

        // ── PASO 1: bytes → texto UTF-8 (envuelto en try por si los bytes son basura) ──
        string? rawText = null;
        try { rawText = Encoding.UTF8.GetString(raw.Span); }   // Encoding.UTF8.GetString [.NET]
        catch { rawText = null; }                              // si no es UTF-8 válido → null

        // ── PASO 1b: texto → JSON, y sacar el campo "AnalyzerType" ──
        string? analyzerType = null;
        JsonDocument? parsed = null;                           // JsonDocument [.NET] = un JSON parseado
        if (rawText is not null)                               // "is not null" [C#] = "si NO es null"
        {
            try
            {
                parsed = JsonDocument.Parse(rawText);          // JsonDocument.Parse [.NET] — puede tirar si no es JSON
                if (parsed.RootElement.TryGetProperty("AnalyzerType", out var t) && t.ValueKind == JsonValueKind.String)
                    // └─ TryGetProperty [.NET] = "¿existe el campo AnalyzerType? si sí, dámelo en 't'"
                    analyzerType = t.GetString()?.Trim();      // GetString [.NET] + Trim [.NET] (saca espacios)
            }
            catch { parsed = null; }                           // si no era JSON → null
        }

        // ── PASO 2: buscar el plugin para ese tipo de equipo ──
        var plugin = analyzerType is null ? null : _plugins.FindForType(analyzerType);
        // └─ el "? :" es el operador ternario. Si analyzerType es null → plugin null; si no → buscarlo.
        //    FindForType [NUESTRO] = busca en el registry un parser que soporte ese AnalyzerType.

        // ── PASO 2b: si no hay tipo o no hay plugin → CUARENTENA y salir ──
        if (analyzerType is null || plugin is null)            // || [C#] = "O"
        {
            // armar el motivo del descarte (para el log/evento)
            var reason = analyzerType is null
                ? (rawText is null ? "invalid UTF-8 in payload"      // no se pudo decodificar
                    : parsed is null ? "invalid JSON"                // no era JSON
                    : "missing AnalyzerType field")                  // JSON ok pero sin AnalyzerType
                : $"no plugin registered for AnalyzerType '{analyzerType}'";  // tipo desconocido
            var saved = await _files.WriteUnknownPayloadAsync(raw, analyzerType, sourceIp, reason, now, ct);
            // └─ WriteUnknownPayloadAsync [NUESTRO] = guarda el payload sospechoso en la carpeta unknown/
            _events.Publish(new DomainEvent.UnknownPayloadReceived(   // avisa al frontend
                AnalyzerType: analyzerType,
                SourceIp: sourceIp,
                Bytes: (ulong)raw.Length,       // (ulong) [C#] = convertir el largo a entero grande
                Reason: reason,
                SavedPath: saved.Path));
            parsed?.Dispose();                  // ?.Dispose() [C#/.NET] = "si parsed no es null, liberalo"
            return false;                       // descartada
        }
        parsed?.Dispose();                      // ya no necesitamos el JSON crudo

        // ── PASO 3: el plugin convierte el mensaje en un objeto Sample ──
        Sample sample;
        try
        {
            sample = plugin.ParseMessage(raw, sourceIp, now);   // ParseMessage [NUESTRO] — lógica específica del equipo
        }
        catch (DomainException e)               // DomainException [NUESTRO] = error de negocio
        {
            _events.Publish(new DomainEvent.PluginParseFailed(analyzerType, e.Message));
            throw;                              // throw [C#] = relanza el error hacia arriba (el TcpServer lo maneja)
        }

        // si el equipo no mandó un id de muestra → generamos uno determinístico
        if (string.IsNullOrWhiteSpace(sample.SampleIdentifier))     // string.IsNullOrWhiteSpace [.NET]
            sample.SampleIdentifier = SynthesizeSampleId(sample);  // SynthesizeSampleId [NUESTRO] (abajo)

        // ── PASO 4: registrar/actualizar el equipo que mandó la muestra ──
        await UpsertInstrumentAsync(sample, analyzerType, sourceIp, now, ct);  // [NUESTRO] (abajo)

        // ── PASO 5: DEDUPLICACIÓN — ¿ya teníamos esta muestra? ──
        if (await _samples.ExistsForRunAsync(sample.Serial.AsString, sample.SampleIdentifier, sample.StartAt, ct))
        {
            // └─ ExistsForRunAsync [NUESTRO] = ¿existe una muestra con este serial+id+inicio?
            //    Necesario porque si el equipo no recibe respuesta, REINTENTA y mandaría duplicados.
            _events.Publish(new DomainEvent.SampleDuplicateSkipped(
                Serial: sample.Serial.AsString,
                SampleIdentifier: sample.SampleIdentifier));
            return false;                       // duplicada → no la guardamos de nuevo
        }

        // ── PASO 6: PERSISTIR en 3 pasos (cada uno con su manejo de error) ──
        await RunPersistStepAsync("save_sample", sample,
            () => _samples.SaveReceivedSampleAsync(sample, ct));           // a) guardar la muestra (en RAM)
        await RunPersistStepAsync("increment_sample_count", sample,
            () => _instruments.IncrementSampleCountAsync(sample.Serial.AsString, ct));  // b) +1 al contador del equipo
        await RunPersistStepAsync("write_artifacts", sample,
            () => _files.WriteSampleArtifactsAsync(sample, ct));           // c) escribir los archivos (JSON, TXT, CSV)
        // └─ el "() => ..." [C#] es una lambda: "la tarea a ejecutar". RunPersistStepAsync la corre
        //    y si falla, emite PersistenceFailed indicando en qué paso.

        // ── FINAL: avisar al mundo que llegó una muestra nueva ──
        _events.Publish(new DomainEvent.SampleReceived(   // el frontend escucha esto y la muestra aparece en vivo
            Uuid: sample.Uuid,
            Serial: sample.Serial.AsString,
            SampleIdentifier: sample.SampleIdentifier,
            Ibp: sample.Ibp,
            Fbp: sample.Fbp));

        return true;                            // guardada con éxito
    }

    // ══ PIPELINE GEMELO (muestras que llegan por IMPRESIÓN, TCP :631) ═════════
    // Es CASI IDÉNTICO al de arriba. Única diferencia: en vez de parsear JSON y buscar
    // el plugin por AnalyzerType, "olfatea" los bytes crudos (FindForPrint) porque los
    // reportes de impresión no traen un campo AnalyzerType claro. Del PASO 4 en adelante
    // (upsert, dedup, persistir, evento) es exactamente lo mismo.
    public async Task<bool> ProcessPrintMessageAsync(
        ReadOnlyMemory<byte> raw,
        string? sourceIp,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var plugin = _plugins.FindForPrint(raw);   // FindForPrint [NUESTRO] = olfatea los bytes para elegir el parser
        if (plugin is null)
        {
            const string reason = "no print plugin matched";
            var saved = await _files.WriteUnknownPayloadAsync(raw, null, sourceIp, reason, now, ct);
            _events.Publish(new DomainEvent.UnknownPayloadReceived(
                AnalyzerType: null,
                SourceIp: sourceIp,
                Bytes: (ulong)raw.Length,
                Reason: reason,
                SavedPath: saved.Path));
            return false;
        }

        Sample sample;
        try
        {
            sample = plugin.ParsePrintMessage(raw, sourceIp, now);
        }
        catch (DomainException e)
        {
            _events.Publish(new DomainEvent.PluginParseFailed("print", e.Message));
            throw;
        }

        var analyzerType = sample.AnalyzerType;

        if (string.IsNullOrWhiteSpace(sample.SampleIdentifier))
            sample.SampleIdentifier = SynthesizeSampleId(sample);

        await UpsertInstrumentAsync(sample, analyzerType, sourceIp, now, ct);

        if (await _samples.ExistsForRunAsync(sample.Serial.AsString, sample.SampleIdentifier, sample.StartAt, ct))
        {
            _events.Publish(new DomainEvent.SampleDuplicateSkipped(
                Serial: sample.Serial.AsString,
                SampleIdentifier: sample.SampleIdentifier));
            return false;
        }

        await RunPersistStepAsync("save_sample[print]", sample,
            () => _samples.SaveReceivedSampleAsync(sample, ct));
        await RunPersistStepAsync("increment_sample_count[print]", sample,
            () => _instruments.IncrementSampleCountAsync(sample.Serial.AsString, ct));
        await RunPersistStepAsync("write_artifacts[print]", sample,
            () => _files.WriteSampleArtifactsAsync(sample, ct));

        _events.Publish(new DomainEvent.SampleReceived(
            Uuid: sample.Uuid,
            Serial: sample.Serial.AsString,
            SampleIdentifier: sample.SampleIdentifier,
            Ibp: sample.Ibp,
            Fbp: sample.Fbp));

        return true;
    }

    // ─── HELPER: registrar o actualizar el equipo (el PASO 4) ─────────────────
    // private [C#] = solo se usa adentro de esta clase.
    private async Task UpsertInstrumentAsync(
        Sample sample,
        string analyzerType,
        string? sourceIp,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _instruments.FindBySerialAsync(sample.Serial.AsString, ct);
        // └─ FindBySerialAsync [NUESTRO] = buscar el equipo por su nro de serie. Devuelve null si no existe.
        Instrument instrument;                    // Instrument [NUESTRO] = la entidad equipo
        if (existing is not null)                 // ¿ya lo conocíamos?
        {
            existing.Touch(sourceIp, now);        // Touch [NUESTRO] = actualiza su última IP y hora
            instrument = existing;
        }
        else                                      // equipo NUEVO
        {
            instrument = Instrument.NewDiscovered(   // NewDiscovered [NUESTRO] = crea un equipo nuevo
                AnalyzerSerial.Create(sample.Serial.AsString),   // AnalyzerSerial.Create [NUESTRO] valida el serial
                analyzerType,
                sourceIp,
                now);
            _events.Publish(new DomainEvent.InstrumentDiscovered(   // avisa "¡equipo nuevo!"
                Serial: sample.Serial.AsString,
                AnalyzerType: analyzerType,
                Ip: sourceIp));
        }
        await _instruments.UpsertOnContactAsync(instrument, ct);   // guarda (crea o actualiza) en instruments.json
    }

    // ─── HELPER: ejecutar un paso de guardado con manejo de error ─────────────
    // Func<Task> [.NET] = "una función que devuelve un Task" — o sea, la tarea a ejecutar.
    // Recibe el nombre del paso (para el evento de error) y la tarea, la corre, y si falla
    // emite PersistenceFailed diciendo EN QUÉ paso falló. Después relanza el error.
    private async Task RunPersistStepAsync(string stage, Sample sample, Func<Task> step)
    {
        try
        {
            await step();                         // ejecuta la tarea (guardar, incrementar, escribir)
        }
        catch (Exception e)                       // Exception [.NET] = cualquier error
        {
            _events.Publish(new DomainEvent.PersistenceFailed(
                Stage: stage,                     // en qué paso reventó
                Serial: sample.Serial.AsString,
                SampleIdentifier: sample.SampleIdentifier,
                Reason: e.Message));
            throw;                                // relanza → el equipo reintentará
        }
    }

    // ─── HELPER: generar un id cuando el equipo no manda uno ──────────────────
    // static [C#] = no usa las dependencias del objeto (es una función "pura").
    // Arma un id DETERMINÍSTICO (mismo input → mismo id) para que un reintento no
    // genere un id distinto y termine duplicando la muestra.
    private static string SynthesizeSampleId(Sample sample)
    {
        var seed = new StringBuilder();           // StringBuilder [.NET] = arma texto eficientemente
        seed.Append(sample.Serial.AsString);      // serial +
        seed.Append('|');
        seed.Append(sample.StartAt?.ToUnixTimeMilliseconds() ?? 0);   // hora de inicio (o 0 si no hay) +
        seed.Append('|');
        seed.Append(sample.ReceivedAt.ToUnixTimeMilliseconds());      // hora de recepción
        var hash = (ulong)seed.ToString().GetHashCode();              // GetHashCode [.NET] = número a partir del texto
        var token = hash.ToString("x");                              // "x" = a hexadecimal
        return $"auto-{token[..Math.Min(token.Length, 10)]}";
        // └─ token[..N] [C#] = "los primeros N caracteres"  .  Math.Min [.NET] = el menor de dos números
        //    resultado: algo como "auto-3f9a2b1c04"
    }
}
