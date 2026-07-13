# Infrastructure — Persistence

Path: `src/PacCollector.Infrastructure/Persistence/`

Repositorios concretos que persisten samples e instrumentos.

---

## InMemorySampleRepository.cs
Path: `src/PacCollector.Infrastructure/Persistence/InMemorySampleRepository.cs`

Implementación de `ISampleRepository` que guarda **todo en memoria**.
Los samples se pierden al reiniciar el proceso.

**Cuándo usar**: para deploys donde los samples downstream van a parar a un
LIMS persistente y este es solo el paso intermedio. La info crítica (los
archivos en el hotfolder, master.csv) sobrevive el restart porque vive en
disco.

**Cuándo NO usar**: si querés que la UI muestre historial completo entre
restarts, o si tenés requirements de auditoría que requieren consulta
histórica directa al colector. En ese caso reemplazar por una impl SQL.

### Backing store
`ConcurrentDictionary<string, Sample>` indexado por UUID.

### Métodos

#### `SaveReceivedSampleAsync(sample, ct) → Task`
`_byUuid[sample.Uuid] = sample`. Sobreescribe si existía. Thread-safe.

#### `FindByUuidAsync(uuid, ct) → Sample?`
`_byUuid.TryGetValue(uuid, out var s)`.

#### `ExistsForRunAsync(serial, sampleIdentifier, startAt, ct) → bool`
Itera todos los samples y devuelve true si alguno tiene la misma terna.
**O(N)** — si hay millones de samples vivos esto se vuelve un cuello de
botella, pero in-memory normalmente quedan miles.

#### `ListPaginatedAsync(filters, offset, limit, ct) → IReadOnlyList<Sample>`
Filtra (`Matches`), ordena por `ReceivedAt` descendente, skip/take.

#### `CountAsync(filters, ct) → ulong`
Cuenta los que matchean los filtros.

#### `CountReceivedSinceAsync(since, ct) → ulong`
Cuenta los que tienen `ReceivedAt >= since`. Usado para "samples de hoy".

#### `Matches(sample, filters) → bool` (private static)
Predicado de filtros. Solo filtra si el filtro no es null. Lo que define
un filtro `null` es "match all".

---

## JsonInstrumentRepository.cs
Path: `src/PacCollector.Infrastructure/Persistence/JsonInstrumentRepository.cs`

Implementación de `IInstrumentRepository` que persiste a **un archivo JSON**
(`instruments.json` en el DataDir). Sobrevive restarts.

**Diseño**:
- `ConcurrentDictionary<string, Instrument>` como cache in-memory.
- `_writeLock` (`SemaphoreSlim`) para serializar escrituras al disco.
- Cada operación que muta llama a `PersistAsync` que reescribe el archivo
  entero.

### `Load(path) → JsonInstrumentRepository` (static)

Factory que crea el repo cargando desde disco:
1. Si el archivo no existe, devuelve repo vacío.
2. Si existe, lee y deserializa a `List<InstrumentRecord>`.
3. Si falla la deserialización, **renombra el archivo a `*.broken-<ts>`**
   (`TryBackupCorrupt`) y devuelve repo vacío. La idea: no perder data
   silenciosamente — el operador puede recuperar el archivo broken si
   hace falta.

### `UpsertOnContactAsync(instrument, ct) → Task`
`_bySerial[serial] = instrument` + `PersistAsync`.

### `FindBySerialAsync(serial, ct) → Instrument?`
Lookup en el dict.

### `UpdateAliasAsync(serial, alias, ct) → Task`
1. Busca el instrumento. Si no existe, tira `InstrumentNotFoundException`.
2. Llama a `inst.SetAlias(alias)` (mutación de la entidad).
3. `PersistAsync`.

### `ListAllAsync(ct) → IReadOnlyList<Instrument>`
Snapshot ordenado por serial ascendente.

### `IncrementSampleCountAsync(serial, ct) → Task`
1. Lookup. Si no existe, silently no-op (caso raro donde el instrumento se
   borró entre el save_sample y el increment).
2. Llama a `inst.IncrementTotalSamples()` — atómico via `Interlocked`.
3. `PersistAsync`.

### `PersistAsync(ct) → Task` (private)

Escritura atómica:
1. `await _writeLock.WaitAsync(ct)`.
2. Snapshot del dict, ordenado por serial.
3. Serializa con `JsonOptions.Pretty`.
4. Escribe a `{path}.{guid}.tmp` con write-through + fsync.
5. `File.Move(tmp, path, overwrite: true)`.
6. Cleanup del tmp si algo falla.

### `TryBackupCorrupt(path) → void` (private static)
Renombra el archivo corrupto a `{name}.broken-{timestamp}`. Si falla,
silently ignora (best-effort).

### InstrumentRecord (private record)

Versión "JSON-friendly" del `Instrument` (la entidad real es mutable y no
serializa limpio con record).

#### `FromEntity(instrument) → InstrumentRecord` (static)
Mapea la entidad al record.

#### `ToEntity() → Instrument`
Mapea el record de vuelta a la entidad para usar en runtime.

---

## Por qué dos enfoques distintos

- **Samples in-memory**: los samples son muchos (uno por cada ensayo de
  cada equipo, todos los días). Si los persistimos a JSON, el archivo crece
  sin control. Los archivos en disco (json/, samples/, master.csv) son la
  persistencia real. El repo in-memory es solo "lo que está vivo ahora".

- **Instruments en JSON**: los instrumentos son pocos (decenas, no miles).
  Cargar el JSON al boot es trivial. Y como sobreviven restarts, el
  auto-discovery se acumula históricamente — la lista de equipos del lab
  no se vacía al reiniciar.

Si en el futuro se necesita persistencia de samples, agregar
`SqlSampleRepository` en esta carpeta, ponerlo en el DI en `Program.cs`,
listo. **El SampleProcessingService no cambia.**
