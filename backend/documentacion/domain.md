# Domain — modelo del negocio puro

Path: `src/PacCollector.Domain/`

Esta capa define **qué cosas existen en el negocio del colector**. No tiene
ninguna dependencia técnica: ni sockets, ni archivos, ni JSON, ni base de
datos. Es código que sobrevive cualquier cambio de stack.

Contenido:

- [Entities](#entities) — los objetos del negocio.
- [ValueObjects](#valueobjects) — tipos chiquitos con invariantes.
- [Ports](#ports) — interfaces que Domain DEFINE pero NO implementa.
- [Errors](#errors) — excepciones del dominio.

---

## Entities

### Sample.cs
Path: `src/PacCollector.Domain/Entities/Sample.cs`

Representa **una muestra analizada por un equipo PAC**. Es el objeto central
del negocio — todo el sistema gira alrededor de recibir, parsear y persistir
samples.

#### Propiedades

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Uuid` | `string` | Identificador único interno generado al recibir el sample (Guid). |
| `Serial` | `AnalyzerSerial` | Serie del equipo que generó la muestra. |
| `AnalyzerType` | `string` | Tipo de equipo (`OptiPMD`, `OptiDist2`, `OptiFZP`, etc.). |
| `SampleIdentifier` | `string` | ID humano de la muestra (lo carga el operador en el equipo). |
| `Operator` | `string?` | Nombre del operador del lab. |
| `Program` | `string?` | Norma ASTM aplicada (`ASTM D7345`, `ASTM D86`, etc.). |
| `StartAt` | `DateTimeOffset?` | Cuándo empezó el ensayo en el equipo. |
| `EndAt` | `DateTimeOffset?` | Cuándo terminó. |
| `Ibp` | `double?` | Initial Boiling Point (°C). Solo destilación. |
| `Fbp` | `double?` | Final Boiling Point (°C). Solo destilación. |
| `Residue` | `double?` | Residuo en mL. Solo destilación. |
| `Recovery` | `double?` | Recuperado (%). Solo destilación. |
| `FbpVolume` | `double?` | Volumen al FBP. |
| `EndOfTest` | `bool?` | True si el ensayo terminó OK. |
| `AlarmBitmask` | `ulong?` | Bitmask de alarmas que reportó el equipo. |
| `Curve` | `DistillationCurve` | Curva de destilación (vacía si no aplica). |
| `Extra` | `SortedDictionary<string, string>` | Campos específicos del equipo no tipados. |
| `SourceIp` | `string?` | IP desde donde llegó el sample. |
| `ReceivedAt` | `DateTimeOffset` | Cuándo lo recibió el colector. |
| `RawJson` | `string` | El payload crudo, persisitido para auditoría. |

#### Métodos

##### `IsComplete() → bool`
Devuelve `true` si `EndOfTest` está seteado y es `true`. Conveniencia para
saber si el ensayo terminó bien.

##### `HasAlarms() → bool`
Devuelve `true` si `AlarmBitmask` no es null y distinto de cero.

##### `HasCurve() → bool`
Devuelve `true` si la curva tiene al menos un punto.

---

### Instrument.cs
Path: `src/PacCollector.Domain/Entities/Instrument.cs`

Representa **un equipo PAC físico** que el colector descubrió en algún
momento. Persiste entre restarts del colector.

#### Propiedades

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Serial` | `AnalyzerSerial` | Serie del equipo. Identificador único. |
| `AnalyzerType` | `string` | Tipo (`OptiPMD`, etc.). |
| `Alias` | `string?` | Nombre humano configurado por el operador (`LAB-DEST-A`). |
| `LastIp` | `string?` | Última IP desde donde se contactó. |
| `Firmware` | `string?` | Versión de firmware reportada. |
| `FirstSeenAt` | `DateTimeOffset` | Primera vez visto. |
| `LastSeenAt` | `DateTimeOffset` | Última vez contactado. |
| `TotalSamples` | `ulong` | Contador de samples recibidos. Acceso thread-safe. |
| `Enabled` | `bool` | Si está habilitado para recibir samples. |

#### Métodos

##### `IncrementTotalSamples() → ulong`
Incrementa atómicamente `TotalSamples` usando `Interlocked.Increment`. Devuelve
el nuevo valor. **Thread-safe** — soporta múltiples samples del mismo equipo
llegando concurrentes.

##### `NewDiscovered(serial, analyzerType, ip, now) → Instrument` (static)
Factory para crear un instrumento recién descubierto: `FirstSeenAt` y
`LastSeenAt` = `now`, alias null, total samples 0, enabled true. Llamado por
`SampleProcessingService.UpsertInstrumentAsync` cuando llega un sample de un
serial que nunca habíamos visto.

##### `Touch(ip, now) → void`
Actualiza `LastSeenAt` al timestamp dado, y si `ip` es no-null actualiza
`LastIp`. Se llama cada vez que un equipo ya conocido manda un sample.

##### `IsOnline(now, threshold) → bool`
Devuelve `true` si `(now - LastSeenAt) < threshold`. Útil para mostrar en la
UI "este equipo está online" según un umbral configurado.

##### `CanBeDeleted() → bool`
Devuelve **siempre false**. Política: los instrumentos no se borran de la
base, solo se deshabilitan. Mantiene historial.

##### `DisplayName() → string`
Devuelve el `Alias` si está seteado, si no `"{Serial} ({AnalyzerType})"`.

##### `SetAlias(alias) → void`
Setea el alias trim-eando whitespace. Si queda vacío, lo setea a null. Llamado
desde `UpdateInstrumentAliasUseCase`.

---

### PluginInfo.cs
Path: `src/PacCollector.Domain/Entities/PluginInfo.cs`

Record con metadata de un plugin para listarlo en la UI: `Id`, `DisplayName`,
`Version`, `Vendor`, `SupportedTypes`, `Source` (builtin / uploaded), `Enabled`.

---

## ValueObjects

### AnalyzerSerial.cs
Path: `src/PacCollector.Domain/ValueObjects/AnalyzerSerial.cs`

Struct read-only que representa la **serie de un equipo**. Garantiza que la
serie nunca esté vacía ni tenga caracteres que rompan paths.

#### `Create(value) → AnalyzerSerial` (static)
Trimea el value y valida:
- Si queda vacío → tira `InvalidAnalyzerSerialException("empty")`.
- Si contiene `/`, `\` o `\0` → tira `InvalidAnalyzerSerialException("forbidden chars")`.

Caracteres prohibidos porque la serial se usa para construir paths de
archivos (`db/{serial}_{analyzerType}/...`).

#### `AsString → string`
Devuelve la serial como string. Si el struct fue default-construido, devuelve
empty string.

#### Equality
Usa comparación ordinal de strings. Dos `AnalyzerSerial` con el mismo string
son iguales.

---

### DistillationCurve.cs
Path: `src/PacCollector.Domain/ValueObjects/DistillationCurve.cs`

Representa la **curva de destilación**: lista ordenada de puntos
`(PctRecovered, TemperatureC)`. Inmutable.

#### CurvePoint (record)
`record CurvePoint(double PctRecovered, double TemperatureC)`.
Un punto de la curva. `PctRecovered` es el porcentaje recuperado (0-100),
`TemperatureC` la temperatura en grados Celsius.

#### `Create(points) → DistillationCurve` (static)
Valida que cada punto tenga `PctRecovered` entre 0 y 100. Tira
`InvalidCurvePointException` si alguno está fuera de rango. Después ordena
ascendente por `PctRecovered`.

#### `Empty() → DistillationCurve` (static)
Devuelve una curva sin puntos. Usado cuando el equipo no envía curva
(ej. OptiFZP, OptiCPP).

#### `Points → IReadOnlyList<CurvePoint>`
Acceso de solo lectura a los puntos.

#### `IsEmpty → bool`
True si no hay puntos.

#### `Count → int`
Cantidad de puntos.

---

### PacChecksum.cs
Path: `src/PacCollector.Domain/ValueObjects/PacChecksum.cs`

Implementa el checksum que el equipo PAC espera de vuelta en la respuesta
LIMS Ethernet (`SaveCheckSum`).

#### `FromBytes(input) → PacChecksum` (static)
Algoritmo: suma todos los bytes mod 256, hace XOR con 0xFF, suma 1, queda
mod 256 (en otras palabras: complemento a dos). Devuelve formato hex de 4
dígitos (`"00A3"`, `"00FF"`, etc.).

#### `FromString(input) → PacChecksum` (static)
Conveniencia: toma el string en UTF-8 y delega a `FromBytes`.

#### `AsString → string`
Devuelve el checksum hex. `"0000"` por default.

---

### SafeFilename.cs
Path: `src/PacCollector.Domain/ValueObjects/SafeFilename.cs`

Sanitiza strings para usarlos como nombres de archivos. Usado cuando el
sampleId o el alias contienen caracteres que romperían el filesystem.

#### `Sanitize(input) → SafeFilename` (static)
Reemplaza `:`, `/`, `\`, `*`, `?`, `"`, `<`, `>`, `|`, newlines y tabs por
`_`. Colapsa espacios múltiples en uno. Trim. Reemplaza el espacio restante
por `_`. Resultado: un string apto para nombre de archivo en Windows y Unix.

#### `AsString → string`
El string sanitizado.

#### `IsEmpty → bool`
True si quedó vacío.

---

### FieldMeta.cs
Path: `src/PacCollector.Domain/ValueObjects/FieldMeta.cs`

`record FieldMeta(string Label, string Unit, string Group)`. Metadata para
describir un campo en la UI: etiqueta legible, unidad, grupo de pertenencia
(`Identificación`, `Resultado`, `Configuración`). Lo provee cada plugin via
`IInstrumentPlugin.FieldDescriptions`.

---

## Ports (las interfaces que el Domain define)

Estas son las interfaces que `Application` usa para hablar con el mundo
exterior. `Infrastructure` provee las implementaciones concretas.

### IInstrumentPlugin.cs
Path: `src/PacCollector.Domain/Ports/IInstrumentPlugin.cs`

Contrato de un **plugin de equipo**. Cada equipo PAC soportado tiene un
plugin que sabe cómo parsear sus datos.

#### Propiedades
- `Id` — id único del plugin (`optipmd-builtin`, `optifzp-print`, etc.).
- `DisplayName` — nombre legible.
- `Version` — versión del plugin.
- `Vendor` — quién lo escribió.
- `SupportedTypes` — lista de `AnalyzerType` que reconoce.
- `FieldDescriptions` — metadata UI de los campos que produce.
- `IsPrintPlugin` — true si es un plugin de modo print, default false.

#### Métodos

##### `ParseMessage(raw, sourceIp, receivedAt) → Sample`
Parsea bytes en modo LIMS Ethernet (JSON). Default tira excepción para
plugins print.

##### `AcceptsPrintFormat(raw) → bool`
Solo en plugins print. Sniff de los primeros 8KB del payload para detectar
si el plugin reconoce el contenido (típicamente busca un header marker como
`"OptiPMD"`).

##### `ParsePrintMessage(raw, sourceIp, receivedAt) → Sample`
Solo en plugins print. Parsea el payload print/PCL.

---

### ISampleRepository.cs
Path: `src/PacCollector.Domain/Ports/ISampleRepository.cs`

Contrato del repositorio de samples.

#### `SaveReceivedSampleAsync(sample, ct)`
Persiste un sample.

#### `FindByUuidAsync(uuid, ct) → Sample?`
Busca por UUID. Null si no existe.

#### `ExistsForRunAsync(serial, sampleIdentifier, startAt, ct) → bool`
**Dedup check**. Devuelve true si ya hay un sample con esa terna `(serial,
sampleId, startAt)`. Usado por `SampleProcessingService` para no procesar
duplicados.

#### `ListPaginatedAsync(filters, offset, limit, ct) → IReadOnlyList<Sample>`
Lista paginada con filtros opcionales por serial, programa, operador y rango
de fechas.

#### `CountAsync(filters, ct) → ulong`
Cuenta total que matchea los filtros.

#### `CountReceivedSinceAsync(since, ct) → ulong`
Cuántos samples se recibieron desde un timestamp. Usado para "samples de hoy"
en el endpoint `/api/server/status`.

#### SampleQueryFilters (record)
`record SampleQueryFilters(string? Serial, string? Program, string? Operator,
DateTimeOffset? From, DateTimeOffset? To)`. Todos opcionales. Si todos son
null, devuelve todo.

---

### IInstrumentRepository.cs
Path: `src/PacCollector.Domain/Ports/IInstrumentRepository.cs`

Contrato del repositorio de instrumentos.

#### `UpsertOnContactAsync(instrument, ct)`
Inserta o actualiza el instrumento (matched por serial). Se llama cada vez
que llega un sample.

#### `FindBySerialAsync(serial, ct) → Instrument?`
Busca por serial. Null si no existe.

#### `UpdateAliasAsync(serial, alias, ct)`
Cambia el alias del instrumento. Llamado por `UpdateInstrumentAliasUseCase`.

#### `ListAllAsync(ct) → IReadOnlyList<Instrument>`
Lista todos los instrumentos descubiertos.

#### `IncrementSampleCountAsync(serial, ct)`
Incrementa atómicamente el contador de samples del instrumento. Acceso
thread-safe — implementado con `Interlocked.Increment` en la entidad.

---

### IFileWriter.cs
Path: `src/PacCollector.Domain/Ports/IFileWriter.cs`

Contrato del escritor de archivos.

#### `WriteSampleArtifactsAsync(sample, ct)`
Escribe TODOS los archivos asociados a un sample procesado: json crudo, txt
LIMS, reporte legible, curva CSV, master.csv, mirror a `recent/`, y el
hotfolder con el template configurado.

#### `WriteUnknownPayloadAsync(raw, analyzerType?, sourceIp?, reason, receivedAt, ct) → UnknownPayloadSaved`
Persiste un payload que NO se pudo parsear, en `db/_unknown/<bucket>/`. El
bucket es el `analyzerType` si está disponible, `_untyped` si era JSON sin
`AnalyzerType`, o `_invalid` si los bytes no son ni texto. Devuelve el path
donde quedó guardado. Crítico para auditoría — el operador siempre sabe qué
se perdió y por qué.

#### UnknownPayloadSaved (record)
`record UnknownPayloadSaved(string Path)` — info del archivo guardado.

---

### IPluginRegistry.cs
Path: `src/PacCollector.Domain/Ports/IPluginRegistry.cs`

Contrato del registro central de plugins.

#### `FindForType(analyzerType) → IInstrumentPlugin?`
Busca un plugin que soporte ese AnalyzerType para el modo LIMS Ethernet. Itera
todos los plugins activos, devuelve el primero cuyo `SupportedTypes` contenga
el tipo.

#### `FindForPrint(raw) → IInstrumentPlugin?`
Busca un plugin print que reconozca esos bytes. Itera plugins print activos,
llama `AcceptsPrintFormat(raw)` en cada uno. Devuelve el primero que matchea.

#### `List() → IReadOnlyList<PluginInfo>`
Lista metadata de todos los plugins para mostrar en UI.

#### `SetEnabled(id, enabled)`
Habilita o deshabilita un plugin por id (sin sacarlo del registro).

---

### IEventBus.cs
Path: `src/PacCollector.Domain/Ports/IEventBus.cs`

Bus de eventos in-process. Una sola operación.

#### `Publish(evt)`
Publica un evento del dominio. Los suscriptores se enteran (típicamente el
WebSocket que notifica a la UI).

---

### DomainEvent.cs
Path: `src/PacCollector.Domain/Ports/DomainEvent.cs`

Jerarquía de eventos del dominio. Todos son `record`.

| Evento | Cuándo se emite |
|---|---|
| `BeaconReceived(Ip, Ts)` | Cada vez que llega un beacon UDP del equipo. |
| `InstrumentDiscovered(Serial, AnalyzerType, Ip?)` | La primera vez que vemos un serial nuevo. |
| `InstrumentTouched(Serial, Ip?)` | Cada vez que un instrumento ya conocido vuelve a contactar. |
| `SampleReceived(Uuid, Serial, SampleId, Ibp?, Fbp?)` | Sample procesado y persistido OK. |
| `SampleDuplicateSkipped(Serial, SampleId)` | Se recibió un sample que ya existía (dedup). |
| `PluginParseFailed(AnalyzerType, Reason)` | El plugin no pudo parsear el payload. |
| `UnknownPayloadReceived(AnalyzerType?, SourceIp?, Bytes, Reason, SavedPath)` | Llegó algo que ningún plugin reconoció — guardado en `_unknown/`. |
| `PersistenceFailed(Stage, Serial?, SampleId?, Reason)` | Una operación de persistencia (save_sample, increment_count, write_artifacts) tiró excepción. |
| `ServerError(Message)` | Error genérico del server. |

Estos eventos son **el lenguaje de monitoring del sistema**. Cualquier
herramienta externa (Grafana, ELK, etc.) que quiera entender qué hace el
colector se suscribe a estos y los procesa.

---

## Errors

Todos están en `src/PacCollector.Domain/Errors/`. Cada uno hereda de
`DomainException`. El callsite hace `throw new XxxException(...)`; la capa
de arriba captura.

| Excepción | Cuándo se tira |
|---|---|
| `DomainException` | Base abstracta. No se tira directamente. |
| `MalformedMessageException` | El payload no se pudo parsear (JSON mal formado, header faltante, etc.). |
| `InvalidAnalyzerSerialException` | Una serial vino vacía o con chars prohibidos. |
| `InvalidCurvePointException` | Un punto de curva con PctRecovered fuera de [0, 100]. |
| `ConfigInvalidException` | Una config JSON malformada o inválida (settings, spec, template). |
| `ConfigNotInitializedException` | Se intentó leer config antes de inicializarla. |
| `InstrumentNotFoundException` | Se buscó un instrumento por serial y no existe. |
| `SampleNotFoundException` | Se buscó un sample por UUID y no existe. |
| `NoPluginForTypeException` | No hay plugin registrado para ese AnalyzerType. |
| `PluginParseFailedException` | El plugin existe pero falló parseando un payload concreto. |

Estas excepciones son el lenguaje de error del dominio. Si Infrastructure
necesita tirar algo, debe ser una de estas (no `IOException` o
`JsonException` directamente).
