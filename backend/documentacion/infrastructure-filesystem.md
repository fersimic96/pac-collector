# Infrastructure — Filesystem

Path: `src/PacCollector.Infrastructure/Filesystem/`

Escritura de archivos al disco. Es donde se materializa lo que el LIMS de
YPF va a levantar.

---

## FileWriterImpl.cs
Path: `src/PacCollector.Infrastructure/Filesystem/FileWriterImpl.cs`

Implementación de `IFileWriter`. Cuando llega un sample procesado, este
escritor produce **hasta 7 archivos** distintos:

| Archivo | Path | Para qué |
|---|---|---|
| JSON crudo | `db/{serial}_{type}/json/{base}.json` | Auditoría del payload original. |
| TXT LIMS | `db/{serial}_{type}/samples/{base}.txt` | Formato clave-valor para LIMS. Solo destilación. |
| Reporte legible | `db/{serial}_{type}/reports/{base}.legible.txt` | Para que un humano lea. |
| Curva CSV | `db/{serial}_{type}/curves/{base}.curva.csv` | Curva de destilación tabular. |
| latest.txt | `db/{serial}_{type}/latest.txt` | El reporte legible más reciente, sin timestamp. |
| master.csv | `db/{serial}_{type}/master.csv` y/o `db/_global/master.csv` | Una fila por sample, append-only. |
| Hotfolder | `<dirConfigurado>/{filename}` | Archivo para que el LIMS lo levante. Template configurable. |

Además, si `MirrorToRecent` está habilitado, todos los archivos anteriores
se copian a `recent/{serial}_{type}/...`.

### Constructor

```csharp
FileWriterImpl(string dbDir, string recentDir, ConfigStore config,
               IReadOnlyDictionary<string, HotfolderTemplate>? hotfolderTemplates = null)
```

- `dbDir`: raíz para `db/`.
- `recentDir`: raíz para `recent/`.
- `config`: para leer settings + routes/instruments.
- `hotfolderTemplates`: dict de templates Name → HotfolderTemplate. Si null,
  no se usa el path template y se cae al enum HotFolderFormat legacy.

### `WriteSampleArtifactsAsync(sample, ct) → Task`

Punto de entrada. Llamado por `SampleProcessingService` al final del pipeline.

Pasos:

1. **Snapshot config** y settings del instrumento.
2. **Resolve baseName y dbRoot**:
   - `baseName` viene de `BaseFilename(sample, general)` — combina serial,
     sampleId y startAt según los flags de visibilidad de la config.
   - `dbRoot` viene de `ResolveDbRoot(sample, cfg)` — chequea si hay un
     `OutputDir` override en la config del instrumento, si no usa
     `{dbDir}/{serial}_{type}/`.
3. **Crea las subcarpetas** que correspondan según los flags de
   `OutputFormats` (`json/`, `samples/`, `reports/`, `curves/`).
4. **Escribe cada formato** condicionalmente:
   - JSON si `WriteJson`.
   - TXT LIMS si `WriteLimsTxt` Y es OptiPMD (solo destilación).
   - Reporte legible si `WriteLegibleTxt`.
   - Curva CSV si `WriteCurveCsv` Y la curva no está vacía.
5. **Llama a `WriteHotFolderAsync`** para producir el archivo del hotfolder.
6. **Mirror a recent/** si `MirrorToRecent`.
7. **Actualiza `latest.txt`** con el reporte legible (sin timestamp).
8. **Append al master.csv** (por-instrumento + global) bajo `_writeLock`
   para evitar interleaving entre samples concurrentes.

### `WriteUnknownPayloadAsync(raw, analyzerType?, sourceIp?, reason, receivedAt, ct) → UnknownPayloadSaved`

Persiste payloads que no se pudieron parsear, en `db/_unknown/<bucket>/`.

Detalle:
- **Bucket**: el `analyzerType` si está disponible; `_untyped` si era JSON
  sin AnalyzerType; `_invalid` si los bytes no son ni UTF-8.
- **Filename**: `{timestamp}_{ip}.{ext}`. La extensión es `.json` si los
  bytes son UTF-8 válido, `.bin` si no.
- Además escribe un `.meta.json` paralelo con info de contexto: cuándo
  llegó, analyzerType (si se sabía), sourceIp, razón del fallo, bytes.

Devuelve `UnknownPayloadSaved(path)` con la ruta del archivo guardado.

### Métodos de formato (todos `internal static`)

#### `LimsClassicText(sample, delimiter, eol, showKey, showUnit) → string`
Genera el formato clave-valor clásico LIMS: `Key{delim}Value{delim}{eol}`.
Una línea por campo. Incluye `AnalyzerType`, `SerialNumber`, `SampleId`,
operator, program, IBP, FBP, recovery, residue, puntos de la curva
(`Recovered_0005`, etc.), y los campos del Extra dictionary.

#### `LegibleReport(sample) → string`
Reporte multilínea para humanos. Incluye `IDENTIFICACIÓN`, `TIEMPOS`,
`RESULTADOS`, `CURVA DE DESTILACIÓN` como secciones con separadores.

#### `CurveCsv(sample) → string`
CSV de la curva con header `AnalyzerSN`, `SampleID`, `%Recuperado,
Temperatura (°C)`. Si el sample tiene IBP, lo emite como `0 (IBP),value`.
Si tiene Fbp + FbpVolume, emite `{volume} (FBP),{fbp}` al final.

#### `SampleAllCsv(sample, alias) → string`
CSV de 2 columnas `Key;Value` con TODOS los campos del sample, los puntos
de la curva, las entradas del Extra, y los campos de tiempo (ReceivedAt,
StartAt, EndAt, SourceIP).

#### `LimsEthernetTxt(sample, alias) → string`
Formato propio del LIMS legacy (estilo serial-over-Ethernet). Multilínea
con etiquetas en inglés: `Status: C`, `Instrument`, `Probe serial number`,
`Sample`, `Start of distillation`, `Standard`, `Percent recovery`, etc.
Termina con `IBP value` + curva + `FBP value`.

#### `MasterRow(sample) → string`
Una fila para el master.csv: timestamp, serial, type, sampleId, operator,
program, startAt, endAt, ibp, fbp, residue, recovery, fbpVolume. CSV
delimitado por coma.

### `WriteHotFolderAsync(sample, cfg, baseName, isDistillation, ct) → Task` (private)

**El método clave del hotfolder.** Determina qué escribir y dónde.

1. Lee la route por serial específico (`cfg.InstrumentRoutes[serial]`). Si
   no hay, cae a settings por tipo (`cfg.Instruments[type]`).
2. Extrae `HotFolderDir`, `Alias`, `HotFolderFormat`, `HotFolderTemplate`.
3. Si no hay `HotFolderDir`, sale (no hay hotfolder configurado).
4. Crea el directorio.
5. **Path nuevo: si hay HotFolderTemplate y el template existe en `_templates`**:
   - Lo renderea con `HotfolderTemplateRenderer.Render(template, sample, alias)`.
   - `payload = (rendered.Filename, rendered.Body)`.
6. **Fallback legacy**: si no hay template, switch sobre el enum
   `HotFolderFormat`:
   - `LimsEthernet` + isDistillation → `LimsEthernetTxt`.
   - `CsvAll` → `SampleAllCsv`.
   - `Csv` → `CurveCsv`.
7. Escribe el archivo atómicamente.

### Métodos privados auxiliares

- `ResolveDbRoot(sample, cfg)` — usa `InstrumentSettings.OutputDir` si existe.
- `ResolveRecentRoot(sample, cfg)` — análogo para recent.
- `InstrumentFolderName(sample)` — `"{serial}_{type}"` sanitizado.
- `BaseFilename(sample, cfg)` — combina serial + sampleId + startAt según los
  flags de visibilidad.
- `EnsureDirsAsync(folderRoot, formats, ct)` — crea las subcarpetas
  necesarias según los flags.
- `TryCopy(src, dst)` — copia best-effort para el mirror a recent.
- `AppendMasterCsvAsync(path, row, ct)` — append-only con header en la
  primera escritura, write-through + fsync para durability.
- `CsvEscape(s)` — escapa `;`, `"`, newlines según RFC.
- `FormatNumber(v)` — shortest round-trippable repr de un double (`"R"`).

---

## AtomicWriter.cs
Path: `src/PacCollector.Infrastructure/Filesystem/AtomicWriter.cs`

Helper para escrituras atómicas: tmp file + flush + rename. Evita que un
power-loss deje archivos a la mitad.

### `WriteAllTextAsync(path, content, ct) → Task` (static)
1. Crea el archivo destino con `{path}.{guid}.tmp`.
2. Escribe el content con write-through y `Flush(flushToDisk: true)`.
3. `File.Move(tmp, path, overwrite: true)`.
4. Si falla, intenta borrar el tmp.

### `WriteAllBytesAsync(path, bytes, ct) → Task` (static)
Misma idea pero con bytes en vez de texto. Usado para guardar payloads
binarios en `_unknown/`.

---

## Por qué importa el AtomicWriter

Garantiza una invariante crítica: **el archivo en el path destino o existe
completo o no existe** — nunca queda a medio escribir. Esto importa para
el hotfolder porque el LIMS de YPF lo lee de forma asíncrona, y si toma
un archivo a medio escribir va a parsear basura.
