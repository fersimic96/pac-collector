# Infrastructure — Config

Path: `src/PacCollector.Infrastructure/Config/`

Configuración del sistema: persistencia atómica del `settings.json`,
estructuras de config (general settings, formatos de output, por-instrument
settings, routes por serial).

---

## ConfigStore.cs
Path: `src/PacCollector.Infrastructure/Config/ConfigStore.cs`

Carga `settings.json` al arrancar, expone snapshots thread-safe, escribe
cambios atómicamente.

### `Load(path) → ConfigStore` (static)

Factory:
1. Si el archivo no existe → devuelve store con config default (`new AppConfig()`).
2. Si existe pero falla la lectura → devuelve default sin tocar nada.
3. Si lee OK pero parsea mal → **renombra el archivo a `*.broken-<ts>`**
   con `TryBackupCorrupt` y devuelve default. Misma filosofía que el
   repositorio de instrumentos: no perder datos silenciosamente.
4. Si parsea con versión futura (`config.version > AppConfig.CurrentVersion`),
   tira `InvalidDataException` con mensaje explícito de "actualizá la app".

### `Snapshot() → AppConfig`
Bajo lock, devuelve un **clone defensivo** del `AppConfig` actual. El caller
puede mutar el clone sin afectar el store. Esto es importante: los
consumers (ej. FileWriter) toman snapshots al inicio de cada operación.

### `Replace(newConfig) → void`
Reemplaza la config completa:
1. Crea directorio padre si hace falta.
2. **Clone defensivo del input** para que el caller no pueda mutar después.
3. Serializa a JSON pretty.
4. Escribe a `{path}.{guid}.tmp` con write-through + fsync.
5. `File.Move(tmp, path, overwrite: true)`.
6. Actualiza el field bajo lock.
7. Dispara el evento `Changed?.Invoke(this, owned.Clone())`.

### `Changed` (event)
Cualquier suscriptor se entera de cambios de config. Usado por el
`ListenerManager` para recargar IPs / puertos cuando cambien.

### `WriteAtomically(tmp, content) → void` (static private)
Helper de write-through:
- `FileMode.Create`, `FileShare.None`, `FileOptions.WriteThrough`.
- `StreamWriter.Flush()` + `fs.Flush(flushToDisk: true)`.

### `ParseWithMigration(raw) → AppConfig` (static private)
1. Parsea el JSON con `AllowTrailingCommas` y `CommentHandling.Skip` (permite
   comentarios JSON5-style para que un humano pueda editar).
2. Lee el campo `version`.
3. Si versión > CurrentVersion → tira.
4. Si versión == CurrentVersion o no está → deserializa directo a AppConfig.

Hoy es solo una validación de versión. Cuando exista v2, acá se hará la
migración.

### `TryBackupCorrupt(path)` (static private)
Renombra `path` a `{name}.broken-{timestamp}`. Best-effort.

---

## AppConfig.cs
Path: `src/PacCollector.Infrastructure/Config/AppConfig.cs`

Estructura completa de la configuración del sistema.

```csharp
class AppConfig
{
    public uint Version { get; set; } = 1;
    public GeneralSettings General { get; set; }
    public OutputFormats OutputFormats { get; set; }
    public Dictionary<string, InstrumentSettings> Instruments { get; set; }
    public Dictionary<string, InstrumentRoute> InstrumentRoutes { get; set; }

    public const uint CurrentVersion = 1;

    public AppConfig Clone() { ... }
}
```

### Propiedades

| Propiedad | Tipo | Para qué |
|---|---|---|
| `Version` | `uint` | Versión del schema. Para migraciones futuras. |
| `General` | `GeneralSettings` | Settings globales (puertos, IPs, eol, etc.). |
| `OutputFormats` | `OutputFormats` | Qué archivos escribir (flags). |
| `Instruments` | `Dict<string, InstrumentSettings>` | Settings POR TIPO de instrumento. Key = analyzerType. |
| `InstrumentRoutes` | `Dict<string, InstrumentRoute>` | Settings POR SERIAL específico. Key = serial. |

### Por qué dos niveles de configuración

- **`Instruments[type]`**: configuración default para todos los equipos de
  ese tipo. Ej: "todos los OptiPMD usan el formato lims-ethernet-txt".
- **`InstrumentRoutes[serial]`**: override por serial. Ej: "el OptiPMD
  serial 1216, que está en el LAB-A, va a un hotfolder distinto".

El FileWriter mira route primero, después settings. Esto permite reglas
generales con excepciones puntuales.

---

## GeneralSettings.cs
Path: `src/PacCollector.Infrastructure/Config/GeneralSettings.cs`

Settings globales. Defaults sensatos para producción.

| Propiedad | Default | Para qué |
|---|---|---|
| `Delimiter` | `";"` | Delimiter usado en LimsClassicText. `"TAB"` se traduce a `\t`. |
| `Eol` | `"<none>"` | End-of-line: `"CR"`, `"LF"`, `"CR-LF"`, `"<none>"` (vacío). |
| `ShowKey` | `true` | Si las líneas LIMS llevan `Key{delim}Value` o solo `Value`. |
| `ShowUnit` | `false` | Si se muestran unidades. |
| `ShowAnalyzerSn` | `true` | Si el baseName incluye el serial. |
| `ShowSampleId` | `true` | Si el baseName incluye el sampleId. |
| `ShowStartTime` | `true` | Si el baseName incluye el startAt. |
| `DbDir` | `null` | Override del directorio de samples (default: DataDir/db). |
| `RecentDir` | `null` | Override del directorio recent. |
| `RecentKeep` | `50` | Cuántos archivos mantener en recent/ (rotation pendiente). |
| `SelectedIp` | `null` | IP que va en el ACK UDP. Si null, autodetect. |
| `AutoStartServer` | `true` | Si los listeners arrancan automáticamente al boot. |
| `PrintServerEnabled` | `false` | Si el PrintServer también arranca al boot. |
| `PrintPort` | `631` | Puerto del PrintServer. |

### EolTranslator (clase estática)
#### `Translate(eol) → string`
Convierte el string de config (`"CR"`, `"LF"`, `"CR-LF"`, otra cosa) al
carácter literal correspondiente (`"\r"`, `"\n"`, `"\r\n"`, `""`).

---

## OutputFormats.cs
Path: `src/PacCollector.Infrastructure/Config/OutputFormats.cs`

Flags que controlan qué archivos escribir:

| Propiedad | Default | Archivo asociado |
|---|---|---|
| `WriteJson` | `true` | `db/.../json/{base}.json` con el RawJson. |
| `WriteLimsTxt` | `true` | `db/.../samples/{base}.txt` formato LIMS. |
| `WriteLegibleTxt` | `true` | `db/.../reports/{base}.legible.txt` para humanos. |
| `WriteCurveCsv` | `true` | `db/.../curves/{base}.curva.csv`. |
| `WriteMasterCsv` | `true` | `db/.../master.csv` per-instrument. |
| `WriteGlobalMasterCsv` | `true` | `db/_global/master.csv` consolidado. |
| `MirrorToRecent` | `true` | Copia paralela a recent/ con los últimos N archivos. |

Para ahorrar espacio o velocidad de escritura, se pueden apagar flags
selectivamente.

---

## InstrumentSettings.cs
Path: `src/PacCollector.Infrastructure/Config/InstrumentSettings.cs`

Configuración por tipo de instrumento (key = analyzerType).

| Propiedad | Tipo | Para qué |
|---|---|---|
| `Enabled` | `bool` | Si este tipo está habilitado. Si false, se ignoran sus samples. |
| `Alias` | `string?` | Alias humano del tipo. |
| `OutputDir` | `string?` | Override del directorio de outputs. |
| `RecentDir` | `string?` | Override de recent. |
| `ShowKey` | `bool?` | Override de la setting global. |
| `ShowUnit` | `bool?` | Override de la setting global. |
| `SelectedParameters` | `List<string>?` | Lista de campos a incluir (filtrado custom). |
| `HotFolderDir` | `string?` | Carpeta donde dejar el archivo del hotfolder. |
| `HotFolderFormat` | `HotFolderFormat?` | Formato legacy (enum). |
| `HotFolderTemplate` | `string?` | Nombre del template (nuevo). Tiene precedencia sobre HotFolderFormat si está seteado. |

---

## InstrumentRoute.cs
Path: `src/PacCollector.Infrastructure/Config/InstrumentRoute.cs`

Configuración por serial específico (override del InstrumentSettings).

| Propiedad | Tipo | Para qué |
|---|---|---|
| `HotFolderFormat` | `HotFolderFormat?` | Override del formato (enum). |
| `HotFolderDir` | `string?` | Override de la carpeta. |
| `Alias` | `string?` | Alias específico de este serial. |
| `HotFolderTemplate` | `string?` | Override del template (nuevo). |

---

## HotFolderFormat.cs
Path: `src/PacCollector.Infrastructure/Config/HotFolderFormat.cs`

Enum legacy con los 3 formatos hardcoded antes del template engine:

```csharp
enum HotFolderFormat { LimsEthernet, CsvAll, Csv }
```

- `LimsEthernet` → `LimsEthernetTxt` (solo para destilación).
- `CsvAll` → `SampleAllCsv` (key-value).
- `Csv` → `CurveCsv` (curva only).

**Sigue siendo soportado** como fallback. Si una route tiene
`HotFolderTemplate` seteado, ese gana; si no, se usa este enum.

---

## JsonOptions.cs
Path: `src/PacCollector.Infrastructure/Config/JsonOptions.cs`

Centraliza las opciones JSON usadas en todo el repo.

### `Default → JsonSerializerOptions` (static readonly)
- `PropertyNamingPolicy = CamelCase`
- `PropertyNameCaseInsensitive = true`
- `AllowTrailingCommas = true`
- `ReadCommentHandling = Skip`
- `Converters`: `JsonStringEnumConverter` (CamelCase)

### `Pretty → JsonSerializerOptions` (static readonly)
Igual que Default + `WriteIndented = true`. Para escribir archivos JSON
legibles.

---

## Cómo se ve un settings.json real

```json
{
  "version": 1,
  "general": {
    "delimiter": ";",
    "eol": "<none>",
    "autoStartServer": true,
    "printServerEnabled": true,
    "printPort": 6310
  },
  "outputFormats": {
    "writeJson": true,
    "writeLimsTxt": true,
    "writeLegibleTxt": true,
    "writeCurveCsv": true,
    "mirrorToRecent": true
  },
  "instruments": {
    "OptiPMD": {
      "enabled": true,
      "alias": "PMD-tipo",
      "hotFolderDir": "C:/LIMS/hotfolder/pmd/",
      "hotFolderTemplate": "lims-ethernet-txt"
    }
  },
  "instrumentRoutes": {
    "1216": {
      "alias": "PMD-LAB-A",
      "hotFolderDir": "C:/LIMS/hotfolder/lab-a/",
      "hotFolderTemplate": "csv-all"
    }
  }
}
```

El OptiPMD serial 1216 va a `lab-a/` con formato `csv-all`. Los otros
OptiPMD van a `pmd/` con `lims-ethernet-txt`.
