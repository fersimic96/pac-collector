# Infrastructure — Hotfolder

Path: `src/PacCollector.Infrastructure/Hotfolder/`

Motor de templates JSON para producir el archivo del hotfolder que el LIMS
de YPF levanta. Es el output que conecta el colector con el resto del
ecosistema de información del laboratorio.

---

## HotfolderTemplate.cs
Path: `src/PacCollector.Infrastructure/Hotfolder/HotfolderTemplate.cs`

Schema declarativo de un template de output. Permite definir formatos
nuevos drop-eando un `.json` en `DataDir/hotfolder-templates/`.

### Propiedades

| Propiedad | Tipo | Para qué |
|---|---|---|
| `Name` | `string` | Nombre único. Por este nombre se lo referencia desde InstrumentRoute. |
| `FilenameTemplate` | `string` | Template del nombre del archivo (acepta tokens). |
| `Encoding` | `string` | `"utf-8"` por default. |
| `LineEnding` | `string` | `"LF"` o `"CRLF"`. |
| `TrimEmptyLines` | `bool` | Si true, líneas vacías resultantes no emiten EOL. |
| `Lines` | `List<string>` | Las líneas del archivo. Cada una puede tener tokens. |

### Sintaxis de los templates

#### Tokens básicos
`{Path}` → valor del campo. Ej: `{Serial}` → `"1216"`.

`{Path:format}` → con format .NET. Ej: `{Ibp:F2}` → `"50.50"`.

`{Path|Fallback}` → fallback chain. Ej: `{Alias|AnalyzerType}` → usa Alias
si no es null, si no AnalyzerType.

`{Path|Fallback|Literal}` → último segmento sin resolver se trata como
literal. Ej: `{StartAt:yyyy-MM-dd|NaN}` → `"NaN"` si StartAt es null.

#### Paths conocidos

| Path | Devuelve |
|---|---|
| `Serial` | Serial del equipo. |
| `AnalyzerType` | Tipo. |
| `SampleIdentifier` | ID de la muestra. |
| `Operator` | Operador (null si no hay). |
| `Program` | Programa/norma. |
| `Alias` | Alias configurado (null si no hay). |
| `Ibp`, `Fbp`, `Residue`, `Recovery`, `FbpVolume` | Doubles, null si no hay. |
| `EndOfTest` | `"true"` o `"false"` o null. |
| `StartAt`, `EndAt`, `ReceivedAt` | Fechas. |
| `SourceIp` | IP. |
| `Extra.XXX` | Campo del Extra dictionary por key. |

#### Línea condicional
`?{Path}?contenido` → la línea se emite SOLO si `{Path}` resuelve a no-null
y no-empty. Si null → se skipea la línea entera.

Ej: `?{Operator}?Operator;{Operator}` — solo emite la línea si hay
operator.

#### ForEach
`{Curve.ForEach: <row-template>}` → expande a N líneas, una por punto de la
curva. Dentro del row-template hay variables especiales:
- `{PctRecovered}` — porcentaje (5, 10, 50, 95.5, etc.).
- `{PctLabel}` — formato `"5%"` o `"12.5%"`.
- `{PctPadded4}` — `"0005"`, `"0095"` (zero-padded a 4).
- `{TemperatureC}` — temperatura.

`{Extra.ForEach: <row-template>}` → expande por cada entrada del Extra
dict. Variables especiales:
- `{Key}` — la clave.
- `{Value}` — el valor.

> El campo `hpgl_curve` (bloque binario) se EXCLUYE automáticamente del
> Extra.ForEach.

---

## HotfolderTemplateLoader.cs
Path: `src/PacCollector.Infrastructure/Hotfolder/HotfolderTemplateLoader.cs`

Carga templates desde dos fuentes:
1. **Embedded resources** del assembly
   (`PacCollector.Infrastructure.Hotfolder.Templates.*.json`).
2. **Override en disco**: `DataDir/hotfolder-templates/*.json`.

Override pisa por Name. Override es **tolerante** a JSON malo (skip +
warning), embedded es strict.

### `LoadAll(overrideDir?) → IReadOnlyList<HotfolderTemplate>` (static)

### `LoadFromFile(path) → HotfolderTemplate` (static)
Carga un archivo específico (strict).

### Validación (`ValidateOrThrow`)
Tira `ConfigInvalidException` si:
- `name` vacío.
- `filenameTemplate` vacío.
- `lines` array vacío.
- `lineEnding` no es ni `"LF"` ni `"CRLF"`.

---

## HotfolderTemplateRenderer.cs
Path: `src/PacCollector.Infrastructure/Hotfolder/HotfolderTemplateRenderer.cs`

El **motor de render**. Toma un template + sample + alias, devuelve filename
+ body.

### `RenderResult` (record)
`record RenderResult(string Filename, string Body)`.

### `Render(template, sample, alias) → RenderResult` (static)

1. Crea `RenderContext(sample, alias)`.
2. Renderea el `FilenameTemplate` con substitución de tokens.
3. Determina el EOL (`\n` o `\r\n`) según `LineEnding`.
4. Itera cada línea del template:
   - Llama `RenderLine(line, ctx, eol)`.
   - Si devuelve null → skip (condicional fallida).
   - Si devuelve string vacío y `TrimEmptyLines` → skip.
   - Else → append + eol (si no termina ya en eol).
5. Devuelve filename + body concatenado.

### `RenderLine(line, ctx, eol) → string?` (private)

1. Si la línea empieza con `?`:
   - Busca el `?` de cierre.
   - Evalúa la expresión entre `?...?` con `SubstituteTokens`.
   - Si vacía → return null (skip).
   - Si no → consume el prefijo, sigue con el resto de la línea.
2. Si la línea matchea `{Curve.ForEach: ...}` o `{Extra.ForEach: ...}` →
   `TryExpandForEach` y devuelve la expansión.
3. Si no → `SubstituteTokens(line, ctx)`.

### `SubstituteTokens(template, ctx) → string` (private)

Itera el template buscando `{...}`. Para cada match:
- Extrae la expresión interna.
- Resuelve con `ResolveExpression`.
- Append el resultado.

### `ResolveExpression(expr, ctx) → string?` (private)

Splittea por `|` para fallbacks. Para cada parte:
- Splittea por `:` para extraer format.
- Llama `ResolvePath(path, format, ctx)`.
- Si devuelve no-null → es el resultado.
- Si todos fallan Y hay >1 segmento → el último se trata como literal.
- Si hay 1 solo segmento sin resolver → null (no devolver el path name como
  literal accidental).

### `ResolvePath(path, format, ctx) → string?` (private)

- Si empieza con `Extra.` → lookup en `sample.Extra`.
- Si hay CurvePoint en contexto y path matchea `PctRecovered` /
  `PctLabel` / `PctPadded4` / `TemperatureC` → devuelve ese.
- Si hay ExtraEntry en contexto y path matchea `Key` / `Value` → devuelve
  ese.
- Si no, switch sobre paths conocidos del Sample.

### `TryExpandForEach(line, ctx, eol) → string?` (private)
Reconoce `{Curve.ForEach: <row>}` o `{Extra.ForEach: <row>}`:
- Itera la colección.
- Para cada item crea un sub-context con esa entrada.
- Renderea el row template con ese context.
- Junta con eol.

### RenderContext (private class)
Lleva el `Sample`, el `alias?`, y opcionalmente el `CurvePoint` o
`(string, string)` de Extra entry actual.

Métodos:
- `WithCurvePoint(p)` — devuelve nuevo context con el point.
- `WithExtraEntry(k, v)` — devuelve nuevo context con la entry.

---

## Templates embedded

Los 3 templates built-in viven en
`src/PacCollector.Infrastructure/Hotfolder/Templates/`:

### lims-ethernet-txt.json
Replica el formato del LimsEthernet enum. Multilínea con etiquetas en
inglés (`Status: C`, `Instrument`, `Sample`, etc.). EOL: CRLF.

### csv-all.json
Replica el formato CsvAll. Pares `Key;Value`. Lineas condicionales para los
campos opcionales. EOL: CRLF.

### curve-csv.json
Replica el formato Csv (solo curva). Header `AnalyzerSN`, `SampleID`,
`%Recuperado,Temperatura (°C)`. EOL: LF.

---

## Cómo se conecta con el resto

`FileWriterImpl.WriteHotFolderAsync` lee `InstrumentRoute.HotFolderTemplate`
(o `InstrumentSettings.HotFolderTemplate`):
- Si está seteado y el template existe en `_templates` → renderea con el
  engine y escribe.
- Si no → fallback al enum `HotFolderFormat` con los métodos de format
  hardcoded.

El integrador puede agregar templates custom sin recompilar:
1. Crea un `.json` con la estructura `HotfolderTemplate`.
2. Lo deja en `DataDir/hotfolder-templates/`.
3. Restart del colector (o `/api/plugins/reload` cuando se agregue ese
   endpoint).
4. Configura la route del instrumento apuntando al template por Name.
