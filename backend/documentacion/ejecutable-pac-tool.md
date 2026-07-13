# Ejecutable — PacCollector.Tools (pac-tool)

Path: `src/PacCollector.Tools/`

Binario: `pac-tool` (Linux/Mac) o `pac-tool.exe` (Windows). CLI de autoría
para integrar equipos nuevos al colector **sin tocar código C#**.

---

## Resumen de comandos

| Comando | Para qué |
|---|---|
| `pac-tool print capture` | Capturar un payload real (1 conexión TCP) a un `.bin`. |
| `pac-tool print decode` | Decodificar un `.bin` a texto (con/sin strip PCL). |
| `pac-tool print spec init` | Generar boilerplate JSON spec para un equipo nuevo. |
| `pac-tool print spec test` | Validar un spec contra un `.bin` y reportar coverage. |

---

## Flujo completo de onboarding de un equipo nuevo

```
1. CAPTURE
   pac-tool print capture --port 6310 --output /tmp/optiflash.bin
   # configurás el equipo OptiFlash para imprimir a esa IP:6310
   # corrés un ensayo en el equipo
   # capture guarda lo que recibió y termina

2. DECODE
   pac-tool print decode --input /tmp/optiflash.bin --strip-pcl
   # ves el reporte como texto plano, identificás qué campos hay

3. SPEC INIT
   pac-tool print spec init --analyzer-type OptiFlash --kind labelValue \
                            --header-marker OptiFlash --output optiflash.json
   # genera un JSON boilerplate con campos mínimos

4. EDITAR JSON
   # agregás extraFieldKeys con labels o regex patterns

5. SPEC TEST
   pac-tool print spec test --spec optiflash.json --sample /tmp/optiflash.bin
   # reporta qué campos extrajo bien, cuáles no, qué hay sin declarar
   # iterás hasta 100%

6. DEPLOY
   cp optiflash.json $DATA_DIR/plugins/print/
   # restart el colector (o POST /api/plugins/reload)
   # equipo soportado
```

---

## Program.cs
Path: `src/PacCollector.Tools/Program.cs`

Entry point. Dispatcher de subcomandos. Args parseados a mano (sin
System.CommandLine para evitar dependencia).

Estructura:
```
pac-tool <command> <subcommand> [options]
pac-tool --help
```

Comandos soportados:
- `print` → dispatch a `PrintCommand.RunAsync`.

---

## Commands/Print/PrintCommand.cs

Sub-dispatcher de `pac-tool print`:
- `capture` → `CaptureCommand.RunAsync`.
- `decode` → `DecodeCommand.Run`.
- `spec` → `SpecCommand.RunAsync` (subdispatch).

---

## Commands/Print/CaptureCommand.cs

### `pac-tool print capture --output FILE.bin [--port 6310] [--timeout-sec N]`

Bindea TCP en el puerto especificado, acepta UNA conexión, drena los bytes
hasta close, guarda el blob.

#### Flujo
1. `TcpListener(IPAddress.Any, port).Start()`.
2. `AcceptTcpClientAsync(ct)` con timeout opcional.
3. Lee bytes en chunks de 16KB hasta que el cliente cierre.
4. Escribe al archivo destino.
5. Imprime cantidad de bytes y termina.

#### Exit codes
- `0` — OK.
- `3` — Timeout esperando conexión.
- `64` — Argumentos inválidos.

---

## Commands/Print/DecodeCommand.cs

### `pac-tool print decode --input FILE.bin [--strip-pcl] [--cr-render]`

Lee bytes, decodifica UTF-8, opcionalmente aplica `PclStripper.Strip` y/o
`CrOverwriteRenderer.Render`, escribe a stdout.

#### Flujo
1. `File.ReadAllBytes(input)`.
2. `Encoding.UTF8.GetString(bytes)`.
3. Si `--strip-pcl` → `PclStripper.Strip(text)`.
4. Si `--cr-render` → `CrOverwriteRenderer.Render(text)`.
5. `Console.Write(text)`.

#### Cuándo usar cada flag
- `--strip-pcl`: SIEMPRE para inspeccionar print payloads (limpia los
  escapes PCL).
- `--cr-render`: solo para OptiDist2 (equipos con layout Windows printer
  CR-overwrite).

---

## Commands/Print/SpecCommand.cs

Sub-dispatcher de `pac-tool print spec`:
- `init` → `SpecInitCommand.Run`.
- `test` → `SpecTestCommand.Run`.

---

## Commands/Print/SpecInitCommand.cs

### `pac-tool print spec init --analyzer-type T --header-marker M --kind K [--output FILE.json]`

Genera boilerplate JSON spec con campos mínimos.

#### Args
- `--analyzer-type` — nombre del equipo (`OptiFlash`).
- `--header-marker` — marker en el banner del reporte.
- `--kind` — `labelValue`, `distillation` u `optiDist`.
- `--output` — path destino (default stdout).

#### Defaults inteligentes según kind
- Si `--kind optiDist`:
  - `requiresCrRender = true`.
  - `headerRegexOverride = "{Marker}\\s+(\\d+)"` (sin firmware).
- Otros kinds: defaults estándar.

#### Output
JSON con `id`, `displayName`, `analyzerType`, `vendor`, `version`, `kind`,
`headerMarker`, `headerRegexOverride?`, `headlineLabel: ""`,
`extraFieldKeys: []`, `fieldSpecs: []`, `requiresCrRender`.

El integrador después edita el JSON agregando los mappings.

---

## Commands/Print/SpecTestCommand.cs

### `pac-tool print spec test --spec SPEC.json --sample SAMPLE.bin`

Valida que un spec parsee correctamente un payload capturado.

#### Flujo
1. `PrintPluginSpecLoader.LoadFromFile(spec)` — carga + valida.
2. `File.ReadAllBytes(sample)` — lee el payload.
3. `new ConfigurablePrintPlugin(spec)` — instancia el plugin.
4. `plugin.AcceptsPrintFormat(bytes)` — verifica el header marker.
5. `plugin.ParsePrintMessage(bytes, null, now)` — parsea.
6. **Reporta**:
   - Typed fields extraídos (Serial, IBP, FBP, recovery, etc.).
   - Cada `ExtraFieldKey` declarado: `✓` o `✗ no match`.
   - Coverage total: `12/14 mappings matched`.
   - Fields presentes en `sample.Extra` que NO están declarados en el spec
     (sugieren agregar).

#### Exit codes
- `0` — Spec OK, todo parsea bien.
- `3` — `ParsePrintMessage` tiró excepción.
- `4` — `AcceptsPrintFormat` devolvió false (header no encontrado).
- `5` — Spec JSON inválido.
- `64` — Args inválidos.

#### Output ejemplo

```
Spec: optipmd-print-builtin (kind=Distillation, headerMarker="OptiPMD")
Sample: tests/PacCollector.ParityTests/Fixtures/optipmd_print_1216.bin

✓ AcceptsPrintFormat: HeaderMarker "OptiPMD" detectado
✓ ParsePrintMessage OK

Typed fields:
  serial            = 1216
  analyzerType      = OptiPMD
  sampleIdentifier  = IRAM 2 2024
  operator          = Fer
  ...

Extra fields coverage (14 mappings declarados):
  ✓ HeadSN                   [label] = 24 I8 M0044
  ✗ AtmPrs                   [label] no match — label="AtmPrs"
  ✓ TemperatureSpec          [label] = no specification
  ...

Resumen: 12/14 mappings matched

Fields presentes en Extra pero NO declarados en spec (1):
    FirmwareVersion = 3.02
```

---

## Acceso a internals de Infrastructure

`pac-tool` necesita usar tipos `internal` de Infrastructure
(`PclStripper`, `CrOverwriteRenderer`). Esto se logra con
`InternalsVisibleTo("pac-tool")` en
`PacCollector.Infrastructure.csproj`. El target es el AssemblyName del
exe, no el nombre del proyecto.

---

## Por qué este tool existe

**Antes**: agregar un equipo nuevo era un proyecto de developer — leer
documentación, escribir parser en C#, compilar, deployar. 1-3 días.

**Ahora**: cualquier integrador con perfil técnico medio agrega un equipo
en 30-60 minutos sin tocar C#. El parser engine es genérico, el "perfil del
equipo" vive en un JSON, y `pac-tool` lo guía con feedback inmediato.

**Esto convierte la "extensibilidad" de promesa a realidad operativa**.
