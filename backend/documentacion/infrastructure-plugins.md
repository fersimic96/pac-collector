# Infrastructure — Plugins

Path: `src/PacCollector.Infrastructure/Plugins/`

Los parsers de equipos. Cada equipo soportado tiene un plugin que sabe
cómo extraer un `Sample` de su payload.

Organización:

- `PluginRegistryImpl.cs` — registro central.
- `PacFamilyPlugin.cs` — parser de **LIMS Ethernet** (JSON).
- `Builtin/PacInstrumentSpec.cs` + `Specs/*.json` — specs JSON-driven de los
  equipos LIMS.
- `Print/ConfigurablePrintPlugin.cs` — parser de **modo Print** (PCL).
- `Print/PrintPluginSpec.cs` + `Print/Specs/*.json` — specs JSON-driven de
  print.
- `Print/` helpers: `PclStripper`, `CrOverwriteRenderer`,
  `DistillationTableParser`, `TwoColumnFieldCollector`, `PrintRegex`,
  `LabelMappingExtractor`.

---

## PluginRegistryImpl.cs
Path: `src/PacCollector.Infrastructure/Plugins/PluginRegistryImpl.cs`

Registro central de todos los plugins. Implementa `IPluginRegistry`.

### `LoadBuiltin(limsDir?, printDir?) → PluginRegistryImpl` (static)

Factory que carga todos los plugins al boot:
1. Lee specs LIMS de embedded + `limsDir` (override).
2. Por cada spec LIMS construye `PacFamilyPlugin(spec)`.
3. Lee specs Print de embedded + `printDir` (override).
4. Por cada spec Print construye `ConfigurablePrintPlugin(spec)`.
5. Devuelve el registry con todos los plugins activos.

### `FindForType(analyzerType) → IInstrumentPlugin?`
Itera plugins no-print habilitados. Devuelve el primero cuyo
`SupportedTypes` contiene el `analyzerType` requerido.

### `FindForPrint(raw) → IInstrumentPlugin?`
Itera plugins print habilitados. Llama `AcceptsPrintFormat(raw)` en cada
uno. Devuelve el primero que matchea.

### `List() → IReadOnlyList<PluginInfo>`
Lista metadata de plugins **LIMS solamente** (los print se acceden por sniff
de bytes, no por lookup explícito desde UI).

### `SetEnabled(id, enabled) → void`
Habilita o deshabilita un plugin por ID. El plugin sigue cargado, solo no
se elige en los `FindFor*`.

### `Reload() → void`
Releé los specs de los override dirs y reconstruye la lista de plugins.
Preserva el estado `enabled` de cada plugin que sobreviva al reload (matched
por id). Usado por el endpoint `/api/plugins/reload`.

### `AllPluginIds() → IReadOnlyCollection<string>`
IDs de TODOS los plugins activos. Lo usa el upload endpoint para verificar
que un plugin recién subido aparezca tras reload.

---

## PacFamilyPlugin.cs (LIMS JSON)
Path: `src/PacCollector.Infrastructure/Plugins/PacFamilyPlugin.cs`

Plugin **agnóstico de equipo** para el modo LIMS Ethernet. El perfil de cada
equipo viene de un `PacInstrumentSpec` JSON. Soporta los 7 equipos PAC
distillation actuales.

### Constructor
```csharp
PacFamilyPlugin(PacInstrumentSpec spec)
```

Construye el plugin con un spec específico (`OptiPMD`, `OptiCPP`, etc.).
Cada instancia maneja UN equipo.

### `ParseMessage(raw, sourceIp, receivedAt) → Sample`

1. Decodifica los bytes como UTF-8 strict (tira si hay bytes inválidos).
2. Parsea el JSON.
3. Extrae `AnalyzerType` del root (o usa el del spec si no está).
4. Extrae el `DataDictionary` (o los campos del root si no hay
   DataDictionary).
5. Lee campos comunes: `AnalyzerSerialNumber`, `SampleIdentifier`,
   `OperatorId`, `ProgramName`, `StartRunDate`/`StartRunTime`,
   `EndRunDate`/`EndRunTime`, `IBP`, `FBP`, `Residue`, `Recovery`,
   `FBPvolume`, `EndOfTest`, `DuringRunAlarm`.
6. Para cada clave `Recovered_{pct}`, agrega un punto a la curva.
7. Mete todo lo que sobre en `Sample.Extra`.

---

## Builtin/

### PacInstrumentSpec.cs
Schema declarativo del LIMS Ethernet:

```csharp
class PacInstrumentSpec {
  string PluginId;     // "optipmd-builtin"
  string DisplayName;  // "PAC OptiPMD"
  string AnalyzerType; // "OptiPMD" (key de lookup)
  string Vendor;
  string Version;
  List<PacFieldSpec> FieldSpecs;
}

record PacFieldSpec(string Key, string Label, string Unit, string Group);
```

### PacInstrumentSpecLoader.cs
Loader que combina embedded resources + override en disco
(`DataDir/plugins/lims/*.json`). Override es **tolerante** a JSON malo
(skip + warning). Embedded es strict.

### `LoadAll(overrideDir?) → IReadOnlyList<PacInstrumentSpec>` (static)

### `LoadFromFile(path) → PacInstrumentSpec` (static)
Carga un único spec. Usado para tests.

### BuiltinSpecs.cs
Convenience class con los 7 specs embedded como propiedades estáticas:
`OptiPmd`, `OptiCpp`, `OptiFpp`, `OptiFzp`, `OptiMpp`, `OptiMvd`,
`OptiFuel`.

### Specs/ (.json files)
- `optipmd.json`, `opticpp.json`, `optifpp.json`, `optifzp.json`,
  `optimpp.json`, `optimvd.json`, `optifuel.json`.

Estructura típica:
```json
{
  "pluginId": "optipmd-builtin",
  "displayName": "PAC OptiPMD",
  "analyzerType": "OptiPMD",
  "vendor": "PAC",
  "version": "1.0.0",
  "fieldSpecs": [
    { "key": "IBP", "label": "IBP", "unit": "°C", "group": "Resultado" },
    ...
  ]
}
```

---

## Print/ — plugins de modo Print/IPP

### ConfigurablePrintPlugin.cs
Plugin **agnóstico de equipo** para modo print. Implementa `IInstrumentPlugin`
con `IsPrintPlugin = true`. El perfil de cada equipo viene de un
`PrintPluginSpec` JSON.

### Constructor
```csharp
ConfigurablePrintPlugin(PrintPluginSpec spec)
```

#### `AcceptsPrintFormat(raw) → bool`
Sniff de los primeros 8KB del payload buscando el `HeaderMarker` del spec.

#### `ParsePrintMessage(raw, sourceIp, receivedAt) → Sample`

1. Decodifica los bytes (UTF-8 con replacement char en bytes inválidos).
2. Aplica `PclStripper.Strip(text)` para sacar las secuencias PCL.
3. Si `spec.RequiresCrRender`, aplica `CrOverwriteRenderer.Render(cleaned)`
   para el layout de impresora de dos columnas (OptiDist2).
4. Busca el header con el regex del spec, extrae serial y firmware.
5. Dispatch según `spec.Kind`:
   - `LabelValue` → `ParseLabelValue` (FZP, CPP).
   - `Distillation` → `ParseDistillation` (OptiPMD).
   - `OptiDist` → `ParseOptiDist` (OptiDist2).

#### Private parsers
- `ParseLabelValue(cleaned, rawText, serial, firmware, ...)` — busca cada
  campo con `LabelMappingExtractor.Extract` (label o regex Pattern).
- `ParseDistillation(...)` — usa `TwoColumnFieldCollector.Collect` para los
  campos de header + `DistillationTableParser.Parse` para la curva.
- `ParseOptiDist(cleaned, rendered, ...)` — usa regexes específicos del
  OptiDist2 sobre el texto post-CR-render.

### PrintPluginSpec.cs
Schema del print spec.

```csharp
class PrintPluginSpec {
  string Id;
  string DisplayName;
  string AnalyzerType;
  string Vendor;
  string Version;
  PrintReportKind Kind;        // LabelValue, Distillation, OptiDist
  string HeaderMarker;          // "OptiPMD" — para sniff
  string? HeaderRegexOverride;  // si null, usa "{Marker}\s+S/N:\s*(\S+)\s+V\s+(\S+)"
  string HeadlineLabel;         // "IBP", "Cloud point", etc.
  List<PrintLabelMapping> ExtraFieldKeys;  // mappings label/regex → key
  List<PrintFieldSpec> FieldSpecs;          // metadata UI
  bool RequiresCrRender;        // true para OptiDist
}

class PrintLabelMapping {
  string Label;       // "Stop Temperature"
  string Key;         // "StopTemperature"
  string? Pattern;    // si tiene, usa regex en vez de label
  int Group = 1;      // capture group del regex
}
```

### PrintPluginSpecLoader.cs
Misma idea que el LIMS loader: embedded + override en disco
(`DataDir/plugins/print/*.json`), tolerante a JSON malo en override.

### Specs/ (.json files)
`optipmd-print.json`, `optifzp-print.json`, `opticpp-print.json`,
`optidist2-print.json`.

---

## Print/ helpers

### PclStripper.cs
#### `Strip(input) → string` (static)
Saca las secuencias de escape PCL (`ESC <param>...<final>`) y trunca todo
lo que viene después de `%1BIN;` (bloque HP-GL). El texto resultante es el
reporte legible que el equipo "imprimió".

### CrOverwriteRenderer.cs
#### `Render(text) → string` (static)
Simula el buffer CR-overwrite del driver Windows printer: cada línea lógica
(`\n`) se compone de N segmentos `\r`-separados, cada uno escribe desde
columna 0, último non-space gana. Necesario para OptiDist2 que usa layout
de impresora de dos columnas.

### DistillationTableParser.cs
#### `Parse(cleaned) → DistillationTable` (static)
Parsea la tabla `Recovered / IBP / 5% / 10% / .../ FBP / %R / %r`. Devuelve
un struct con IBP, FBP, recovery_pct, residue_pct, y los puntos de la curva.

### TwoColumnFieldCollector.cs
#### `Collect(text) → Dictionary<string, string>` (static)
Para reportes con dos columnas tipo OptiPMD. Splittea cada línea en
segmentos por `\s{2,}` y por cada segmento parsea `Label: value`.

#### `CleanValue(raw) → string` (static)
Helper público: saca el sufijo ` C` de valores `"232.5 C"`. Saca el char
de reemplazo Unicode (`U+FFFD`) que aparece cuando los bytes no eran UTF-8.

### LabelMappingExtractor.cs
#### `Extract(mapping, text) → string?` (static)
Dispatcher de un `PrintLabelMapping`:
- Si `mapping.Pattern` no es null → evalúa regex en el text, devuelve el
  capture group indicado.
- Si no → busca `^{Label}\s*:\s*(.+?)$` en el text.

### PrintRegex.cs
Conjunto de regexes pre-compilados con source generators
(`[GeneratedRegex]`). Incluye `ResultDate`, `Operator`, `SampleId`,
`Product`, `PmdRunDate`, `OptidistDate`, `OptidistOperator`,
`OptidistRecovery`, etc. Cada regex es un método static partial generado en
compile-time.

### PrintReportKind.cs
```csharp
enum PrintReportKind { LabelValue, Distillation, OptiDist }
```

---

## Cómo agregar soporte para un equipo nuevo

**Opción A: equipo cae en una de las 3 shapes existentes** (típicamente sí
para equipos PAC):
1. Capturar un payload real con `pac-tool print capture`.
2. Generar boilerplate spec con `pac-tool print spec init`.
3. Editar el JSON spec agregando `extraFieldKeys` con labels o patterns.
4. Iterar con `pac-tool print spec test` hasta coverage 100%.
5. Drop el spec en `DataDir/plugins/print/`. Reload o restart.

**Opción B: equipo necesita una shape nueva** (vendor distinto, layout
nuevo):
1. Agregar valor al enum `PrintReportKind`.
2. Crear método `Parse{Newkind}` en `ConfigurablePrintPlugin` con la lógica.
3. Agregar case en el dispatcher.
4. Crear el spec JSON con `kind: "{newkind}"`.

Opción A NO requiere recompilar. Opción B sí.
