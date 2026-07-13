# Application — Services

Path: `src/PacCollector.Application/Services/`

Esta carpeta tiene **un solo archivo** pero es el más importante de toda la
capa Application: `SampleProcessingService`.

---

## SampleProcessingService.cs
Path: `src/PacCollector.Application/Services/SampleProcessingService.cs`

**Es el corazón del sistema.** Toda muestra que entra al colector pasa por
este servicio. Es la orquestación entre: plugins (para parsear), repositorios
(para persistir), file writer (para outputs), y event bus (para anunciar).

### Constructor

```csharp
public SampleProcessingService(
    IPluginRegistry plugins,
    ISampleRepository samples,
    IInstrumentRepository instruments,
    IFileWriter files,
    IEventBus events)
```

Recibe **5 ports** por DI. No conoce ninguna implementación concreta. Todo
lo que va a llamar son interfaces de `Domain.Ports`.

---

### `ProcessRawMessageAsync(raw, sourceIp, ct) → Task<bool>`

**Entry point para LIMS Ethernet** (TCP 9980 con JSON). Devuelve `true` si
el sample se procesó OK, `false` si se descartó (payload desconocido o
duplicado).

#### Los 10 pasos detallados

**Paso 1 — Decodificar bytes a texto UTF-8.**

```csharp
try { rawText = Encoding.UTF8.GetString(raw.Span); }
catch { rawText = null; }
```

Si los bytes no son UTF-8 válido, `rawText` queda en null. No tira excepción
todavía — queremos llegar al paso 4 con esa info para guardar el blob como
"_invalid".

**Paso 2 — Parsear el JSON y extraer AnalyzerType.**

```csharp
parsed = JsonDocument.Parse(rawText);
if (parsed.RootElement.TryGetProperty("AnalyzerType", out var t))
    analyzerType = t.GetString()?.Trim();
```

Si el JSON es inválido, `parsed` queda null. Si no tiene `AnalyzerType`,
queda string vacío. Tampoco tiramos todavía.

**Paso 3 — Buscar el plugin.**

```csharp
var plugin = analyzerType is null ? null : _plugins.FindForType(analyzerType);
```

El registry itera sus plugins LIMS y devuelve el primero que tenga
`analyzerType` en sus `SupportedTypes`. Null si ningún plugin lo soporta.

**Paso 4 — Caso "no sé qué es esto".**

Si `analyzerType` es null o no hay plugin para ese tipo:

```csharp
var reason = analyzerType is null
    ? (rawText is null ? "invalid UTF-8 in payload"
        : parsed is null ? "invalid JSON"
        : "missing AnalyzerType field")
    : $"no plugin registered for AnalyzerType '{analyzerType}'";
var saved = await _files.WriteUnknownPayloadAsync(raw, analyzerType, sourceIp, reason, now, ct);
_events.Publish(new DomainEvent.UnknownPayloadReceived(
    AnalyzerType: analyzerType,
    SourceIp: sourceIp,
    Bytes: (ulong)raw.Length,
    Reason: reason,
    SavedPath: saved.Path));
return false;
```

**Tres cosas importantes acá**:
1. El blob se guarda en `db/_unknown/<bucket>/` para auditoría.
2. Se emite evento `UnknownPayloadReceived` con el detalle.
3. Retornamos `false` — el caller sabe que NO se procesó pero NO hubo
   excepción.

El operador puede revisar `_unknown/` y ver qué se perdió, por qué, desde
qué IP. **Visibilidad completa de lo que falla.**

**Paso 5 — Parsear con el plugin.**

```csharp
try { sample = plugin.ParseMessage(raw, sourceIp, now); }
catch (DomainException e)
{
    _events.Publish(new DomainEvent.PluginParseFailed(analyzerType, e.Message));
    throw;
}
```

El plugin convierte los bytes en una entidad `Sample` ya estructurada. Si
falla, emitimos evento `PluginParseFailed` y propagamos la excepción
(porque ahora sí, algo está roto en el plugin o el payload — no es un
caso de borde).

**Paso 6 — Sintetizar SampleIdentifier si está vacío.**

```csharp
if (string.IsNullOrWhiteSpace(sample.SampleIdentifier))
    sample.SampleIdentifier = SynthesizeSampleId(sample);
```

A veces el operador no carga el sample ID en el equipo. En vez de dejarlo
vacío (lo que rompe la dedup y los nombres de archivo), generamos uno con
un hash determinístico de serial + startAt + receivedAt. **Mismo input →
mismo ID generado** (importante para dedup posterior).

**Paso 7 — Upsert del instrumento.**

```csharp
await UpsertInstrumentAsync(sample, analyzerType, sourceIp, now, ct);
```

Ver método privado más abajo. Auto-discovery del equipo si es nuevo, touch
si ya existía.

**Paso 8 — Dedup check.**

```csharp
if (await _samples.ExistsForRunAsync(sample.Serial.AsString, sample.SampleIdentifier, sample.StartAt, ct))
{
    _events.Publish(new DomainEvent.SampleDuplicateSkipped(
        Serial: sample.Serial.AsString,
        SampleIdentifier: sample.SampleIdentifier));
    return false;
}
```

Preguntamos al repo: "¿ya tenés un sample con esta terna (serial, sampleId,
startAt)?". Si sí, salimos. Esto cubre:
- Equipo que reintenta porque no le llegó el ACK.
- Operador que manda manualmente algo que ya estaba.
- Cualquier escenario donde el mismo sample podría procesarse dos veces.

**Paso 9 — Las 3 operaciones de persistencia.**

```csharp
await RunPersistStepAsync("save_sample", sample,
    () => _samples.SaveReceivedSampleAsync(sample, ct));
await RunPersistStepAsync("increment_sample_count", sample,
    () => _instruments.IncrementSampleCountAsync(sample.Serial.AsString, ct));
await RunPersistStepAsync("write_artifacts", sample,
    () => _files.WriteSampleArtifactsAsync(sample, ct));
```

Cada una envuelta en `RunPersistStepAsync` que captura cualquier excepción,
emite evento `PersistenceFailed(stage, serial, sampleId, reason)`, y propaga.
**No se traga ningún error silenciosamente.**

Orden importante:
1. Save sample primero — si falla, no incrementamos contador.
2. Increment count después — el contador del instrumento sube con cada
   sample persistido.
3. Write artifacts al final — escribir archivos es la operación más lenta
   y la más propensa a fallar por permisos / disco lleno.

**Paso 10 — Anuncio.**

```csharp
_events.Publish(new DomainEvent.SampleReceived(
    Uuid: sample.Uuid,
    Serial: sample.Serial.AsString,
    SampleIdentifier: sample.SampleIdentifier,
    Ibp: sample.Ibp,
    Fbp: sample.Fbp));
return true;
```

Sample procesado OK. El evento lo escucha el WebSocket que notifica a la
UI en tiempo real.

---

### `ProcessPrintMessageAsync(raw, sourceIp, ct) → Task<bool>`

**Entry point para Print over Ethernet** (TCP 631, IPP o raw PCL).

Estructura idéntica a `ProcessRawMessageAsync` pero con 2 diferencias:

**Diferencia 1: cómo encuentra el plugin.**

En vez de leer un campo `AnalyzerType` del JSON (no hay JSON acá), llama:

```csharp
var plugin = _plugins.FindForPrint(raw);
```

El registry itera los plugins print activos y llama
`AcceptsPrintFormat(raw)` en cada uno. Cada plugin sniff-ea los primeros
8KB del payload buscando su header marker (`"OptiPMD"`, `"OptiDist"`, etc.).
Gana el primero que matchea.

**Diferencia 2: método de parseo.**

En vez de `plugin.ParseMessage(...)` usa `plugin.ParsePrintMessage(...)`,
que sabe procesar PCL + texto + HP-GL.

**Pasos 6-10 son IDÉNTICOS**: synthesize, upsert, dedup, persist 3 steps,
emit event. Por eso el sample termina en el mismo lugar y con el mismo
formato downstream — el hotfolder no se entera de por dónde llegó.

---

### `UpsertInstrumentAsync(sample, analyzerType, sourceIp, now, ct) → Task` (private)

Maneja el caso "instrumento que ya conocíamos" vs "instrumento nuevo".

```csharp
var existing = await _instruments.FindBySerialAsync(sample.Serial.AsString, ct);
Instrument instrument;
if (existing is not null)
{
    existing.Touch(sourceIp, now);  // actualiza LastSeenAt y LastIp
    instrument = existing;
}
else
{
    instrument = Instrument.NewDiscovered(
        AnalyzerSerial.Create(sample.Serial.AsString),
        analyzerType,
        sourceIp,
        now);
    _events.Publish(new DomainEvent.InstrumentDiscovered(
        Serial: sample.Serial.AsString,
        AnalyzerType: analyzerType,
        Ip: sourceIp));
}
await _instruments.UpsertOnContactAsync(instrument, ct);
```

**Por qué esto es importante**: hace **auto-discovery** de equipos. La
primera vez que conectan un OptiPMD nuevo al laboratorio, el primer
sample que llegue va a:
1. Crear el `Instrument` en el repo.
2. Emitir `InstrumentDiscovered` que la UI muestra como notificación.
3. El equipo aparece en la lista de instrumentos sin que nadie lo registre
   manualmente.

---

### `RunPersistStepAsync(stage, sample, step) → Task` (private)

Wrapper para las 3 operaciones del paso 9. Captura excepciones, emite
evento, propaga.

```csharp
try { await step(); }
catch (Exception e)
{
    _events.Publish(new DomainEvent.PersistenceFailed(
        Stage: stage,
        Serial: sample.Serial.AsString,
        SampleIdentifier: sample.SampleIdentifier,
        Reason: e.Message));
    throw;
}
```

El `stage` identifica cuál de las 3 operaciones falló:
`"save_sample"`, `"increment_sample_count"`, `"write_artifacts"`, o las
variantes con `[print]` sufijo cuando viene por print mode.

**Patrón clave**: emite el evento ANTES del rethrow. Así el monitoring se
entera, aunque la excepción suba y mate el use case.

---

### `SynthesizeSampleId(sample) → string` (private static)

Genera un identificador determinístico cuando el operador no cargó uno.

```csharp
var seed = new StringBuilder();
seed.Append(sample.Serial.AsString);
seed.Append('|');
seed.Append(sample.StartAt?.ToUnixTimeMilliseconds() ?? 0);
seed.Append('|');
seed.Append(sample.ReceivedAt.ToUnixTimeMilliseconds());
var hash = (ulong)seed.ToString().GetHashCode();
var token = hash.ToString("x");
return $"auto-{token[..Math.Min(token.Length, 10)]}";
```

Concatena `serial|startAt|receivedAt` y hashea. Resultado: `"auto-3f5a2b9c"`
o similar.

**Propiedades**:
- Mismo input → mismo output (determinístico).
- Diferente sample (diferente startAt o receivedAt) → diferente output.
- Imposible que coincida con un sampleId humano (prefijo `auto-`).

> **Nota**: `string.GetHashCode()` no es estable entre procesos en .NET
> Core+. Esto está documentado en `BUG-FINDINGS.md` como un issue
> potencial — si un proceso muere y el sample se reintenta tras restart,
> el ID puede cambiar. Pendiente reemplazar por SHA-256 truncado.

---

## Por qué importa este servicio

Tres propiedades clave que tienen que entender:

1. **Idempotente**. Si el mismo sample llega dos veces (por reintentos del
   equipo, por errores de red, por operador manual), se procesa UNA sola
   vez. La dedup check + el SampleIdentifier determinístico garantizan esto.

2. **Observable**. Cada paso emite eventos auditables: `BeaconReceived`,
   `InstrumentDiscovered`, `InstrumentTouched`, `SampleReceived`,
   `SampleDuplicateSkipped`, `PluginParseFailed`,
   `UnknownPayloadReceived`, `PersistenceFailed`. Cualquier sistema de
   monitoring se suscribe a estos y reconstruye qué pasó con cada sample.

3. **Fail-loud**. Errores no se tragan. Si una operación de persistencia
   falla, emite evento + propaga excepción. El operador se entera, no se
   pierde info silenciosamente.

Estas 3 propiedades hacen el servicio **production-grade** — apto para
correr 24/7 sin que se rompa la trazabilidad ante eventos atípicos.
