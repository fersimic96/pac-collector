# Documentación pac-collector-net

Documentación técnica del proyecto pensada para **entender el código sin tener
que abrirlo todo**. Cada archivo recorre los archivos C# de un área del repo,
clase por clase y método por método, explicando qué hace, cuándo se invoca y
por qué existe.

Si solo querés un overview de 5 minutos, leé [arquitectura.md](./arquitectura.md).
Si querés entender un área concreta, andá directo al archivo correspondiente.

## Índice por capa

### Conceptos

- [arquitectura.md](./arquitectura.md) — Clean Architecture aplicada al
  colector. Las 4 capas, qué puede llamar a qué, por qué importa.
- [glosario.md](./glosario.md) — vocabulario del dominio: LIMS, IPP, PCL,
  hotfolder, beacon, ACK, sample, instrument.

### Domain (la lógica del negocio sin tecnología)

- [domain.md](./domain.md) — entidades (`Sample`, `Instrument`),
  value objects (`AnalyzerSerial`, `DistillationCurve`, etc.), ports
  (interfaces) y errors.

### Application (los casos de uso)

- [application-use-cases.md](./application-use-cases.md) — los 6 use cases:
  recibir sample/print, listar samples/instruments, beacon, update alias.
- [application-services.md](./application-services.md) — el corazón del
  sistema: `SampleProcessingService` función por función.

### Infrastructure (la tecnología)

- [infrastructure-network.md](./infrastructure-network.md) — listeners
  UDP/TCP/Print, IppResponseBuilder, ListenerManager.
- [infrastructure-filesystem.md](./infrastructure-filesystem.md) —
  `FileWriterImpl` con todos sus métodos de output.
- [infrastructure-persistence.md](./infrastructure-persistence.md) —
  `InMemorySampleRepository` y `JsonInstrumentRepository`.
- [infrastructure-config.md](./infrastructure-config.md) —
  `ConfigStore`, `AppConfig`, settings por tipo y route por serial.
- [infrastructure-plugins.md](./infrastructure-plugins.md) — registry,
  `PacFamilyPlugin` (LIMS JSON), `ConfigurablePrintPlugin` (print) + helpers.
- [infrastructure-hotfolder.md](./infrastructure-hotfolder.md) —
  motor de templates de salida hacia el LIMS.

### API HTTP

- [api-endpoints.md](./api-endpoints.md) — los 8 grupos de endpoints REST +
  WebSocket que expone el server.

### Ejecutables (los 4 binarios)

- [ejecutable-shell.md](./ejecutable-shell.md) — el wrapper desktop
  (Photino).
- [ejecutable-pac-tool.md](./ejecutable-pac-tool.md) — CLI de autoría para
  agregar equipos nuevos sin compilar.
- [ejecutable-pac-mock.md](./ejecutable-pac-mock.md) — simulador de equipo
  PAC para tests sin hardware.

## Convención de los docs

Cada archivo sigue este esquema:

```
## NombreArchivo.cs
Path: src/.../NombreArchivo.cs

[Una frase sobre qué hace el archivo en general]

### NombreDeClase
[Qué representa, dependencias clave, ciclo de vida]

#### NombreMetodo(parámetros) → TipoRetorno
[Qué hace en 2-3 frases]
[Cuándo se invoca]
[Qué devuelve y bajo qué condiciones]
```

## Cómo mantener esta documentación

Cuando agreguen una clase o método nuevo, actualizar el archivo que cubre esa
área. Si crean una capa nueva, agregar un archivo `<capa>.md` y lincarlo desde
este README.

La documentación es buena cuando alguien que nunca vio el código puede leer
un archivo y saber qué clase usar y qué método llamar para un caso de uso
dado. Si encuentran algo confuso, **arreglarlo es parte del trabajo**, no
"otra tarea para después".
