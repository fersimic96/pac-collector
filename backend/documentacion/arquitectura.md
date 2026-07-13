# Arquitectura — Clean Architecture

El proyecto sigue **Clean Architecture** (Robert C. Martin) adaptada a un
colector de datos. La esencia es: separar el código en capas concéntricas
donde **las dependencias siempre apuntan hacia adentro**.

## Las 4 capas

```
┌──────────────────────────────────────────────────────────────┐
│  EJECUTABLES (Api / Shell / Tools / MockDevice)              │
│  Composición: registran servicios en DI, levantan listeners,  │
│  exponen endpoints HTTP, parsean CLI args.                   │
├──────────────────────────────────────────────────────────────┤
│  Infrastructure                                              │
│  Implementación concreta de las interfaces que define Domain. │
│  Sockets, archivos, JSON, regex, parsers, repos.              │
├──────────────────────────────────────────────────────────────┤
│  Application                                                 │
│  Casos de uso del negocio. Orquesta el dominio. No sabe       │
│  qué tecnología hay debajo.                                   │
├──────────────────────────────────────────────────────────────┤
│  Domain                                                      │
│  Modelo puro. Entidades, value objects, contratos              │
│  (interfaces). Sin dependencias externas.                     │
└──────────────────────────────────────────────────────────────┘

           ▲                                ▲
           │ dependencias                   │
           │ apuntan hacia adentro          │
```

**Regla de oro**:

- `Domain` no depende de nadie.
- `Application` depende solo de `Domain`.
- `Infrastructure` depende de `Application` y `Domain`.
- Los ejecutables dependen de todas las capas.

Si alguien escribe código que rompe esta regla (ej. una entidad del Domain
que importa System.IO), está rompiendo la arquitectura.

## Por qué importa esta separación

Tres razones concretas:

1. **Testeo barato**. El Domain y la Application se testean sin levantar
   sockets, sin tocar disco, sin base de datos. Mockear las interfaces
   alcanza. Por eso `PacCollector.Domain.Tests` y `PacCollector.Application.Tests`
   son rápidos y deterministas.

2. **Cambio de tecnología sin reescribir el negocio**. Si mañana hay que
   pasar de archivos a Postgres, solo se cambia la clase que implementa
   `ISampleRepository`. El SamplerProcessingService no se entera.

3. **Onboarding rápido**. Un dev nuevo lee el Domain en una tarde y entiende
   "qué cosas hay en el negocio". Después puede explorar las capas externas
   sabiendo dónde está cada cosa.

## Los 7 proyectos de la solución

### Librerías (no se ejecutan solas)

- **PacCollector.Domain**. Entities (Sample, Instrument), Value Objects
  (AnalyzerSerial, DistillationCurve…), Ports (ISampleRepository,
  IFileWriter…), Errors. **Cero dependencias externas.**

- **PacCollector.Application**. Use Cases (ReceiveSampleUseCase,
  ListSamplesUseCase…), Services (SampleProcessingService), DTOs.
  **Depende solo de Domain.**

- **PacCollector.Infrastructure**. Network (UdpServer, TcpServer, PrintServer),
  Filesystem (FileWriterImpl, AtomicWriter), Persistence
  (InMemorySampleRepository, JsonInstrumentRepository), Config, Plugins,
  Hotfolder, EventBus. **Depende de Application y Domain.**

### Ejecutables

- **PacCollector.Api** (`pac-collector` / `PacCollector.Api.exe`). Entry point
  HTTP server. Levanta Kestrel en `127.0.0.1:5174`, registra servicios DI,
  inicia los listeners de protocolo (UDP/TCP/IPP), expone endpoints REST y
  WebSocket. **Es el proceso que corre 24/7 en producción.**

- **PacCollector.Shell**. Wrapper desktop con Photino. Levanta el Api como
  subproceso y abre una ventana WebView que muestra la UI. Para puestos que
  prefieren app de escritorio en vez de ir al browser.

- **PacCollector.Tools** (`pac-tool`). CLI de autoría: capture, decode,
  spec init, spec test. Para que un integrador agregue equipos nuevos sin
  recompilar el colector.

- **PacCollector.MockDevice** (`pac-mock`). Simulador de equipo PAC desde
  línea de comandos. Implementa el lado-equipo de los dos protocolos (LIMS
  Ethernet y Print IPP). Sirve para testing, demos y como spec ejecutable
  del protocolo proprietary.

## Flujo de un sample end-to-end

Para entender cómo las capas trabajan juntas, este es el path de un sample
desde que el equipo lo manda hasta que el archivo aparece en el hotfolder:

```
 1. Equipo PAC envía datos por la red
        │
        ▼
 2. Listener en Infrastructure recibe bytes
    (UdpServer / TcpServer / PrintServer)
        │
        ▼
 3. Listener invoca al UseCase correspondiente
    (ReceiveSampleUseCase / ReceivePrintUseCase)
        │
        ▼
 4. UseCase delega al SampleProcessingService
        │
        ▼
 5. SampleProcessingService:
    - busca el plugin via PluginRegistry
    - parsea los bytes a un Sample (Domain entity)
    - synthesize SampleIdentifier si vino vacío
    - upsert del Instrument (con InstrumentDiscovered si es nuevo)
    - dedup check
    - guarda el sample en el SampleRepository
    - escribe archivos vía FileWriter (json, txt, hotfolder)
    - emite SampleReceived event
        │
        ▼
 6. El hotfolder queda con un archivo que el LIMS de YPF levanta
```

Cada flecha es una **llamada vía interface** (`ISampleRepository.Save`,
`IFileWriter.WriteSampleArtifactsAsync`, `IEventBus.Publish`). El
SampleProcessingService no sabe que Save escribe a memoria, que
WriteSampleArtifactsAsync usa el filesystem, ni que Publish manda eventos
por un Channel. Solo conoce los contratos.

## Reglas de diseño que hay que respetar

1. **No agregar referencias a System.IO, System.Net o System.Text.Json en
   el Domain**. Si ven que pasa, romper el compile.

2. **No instanciar tipos de Infrastructure desde Application**. Application
   recibe sus dependencias vía constructor (DI). Si necesitan algo que no
   está como port en Domain, crear el port primero.

3. **El SampleProcessingService es la única coordinación entre capas
   complejas**. Si aparece otro service que necesita orquestar repos +
   plugins + filesystem, considerá si es realmente un caso de uso nuevo
   o si pertenece dentro de SampleProcessingService.

4. **Cada plugin de equipo es un archivo aparte**. Si agregan soporte para
   un Mettler o un Anton Paar, no metan código en PacFamilyPlugin —
   creen `MettlerPlugin` y registrenlo en el PluginRegistry.

## Para profundizar

- [domain.md](./domain.md) — clases del Domain.
- [application-services.md](./application-services.md) — el
  SampleProcessingService línea por línea.
- [infrastructure-plugins.md](./infrastructure-plugins.md) — cómo se agrega
  un plugin nuevo (en código C# o vía JSON).
