# Glosario

Vocabulario técnico del proyecto y del dominio.

---

### ACK
Acknowledgement. En el protocolo LIMS Ethernet, la respuesta UDP del
colector al beacon del equipo. Formato: `"ACK <ip> <port>"`. Le dice al
equipo dónde abrir la conexión TCP para mandar los datos.

### Alias
Nombre humano configurado para un instrumento, distinto del serial.
Ej: el OptiPMD serial 1216 puede tener alias `"PMD-LAB-DEST-A"`. Se usa
en el filename y en el hotfolder.

### AnalyzerType
Tipo del equipo PAC. Ej: `"OptiPMD"`, `"OptiDist2"`, `"OptiFZP"`,
`"OptiCPP"`. Es la clave de lookup para encontrar el plugin que parsea
los datos.

### ASTM
American Society for Testing and Materials. Define los **métodos
normalizados** que los equipos PAC ejecutan. Ej: `ASTM D86` para
destilación atmosférica, `ASTM D7345` para mini-destilación, `ASTM D93`
para flash point.

### Beacon
Paquete UDP que el equipo PAC manda al puerto 3000 buscando un colector.
Payload exacto: 3 bytes `0x01 0x02 0x03`. Si llega cualquier otra cosa al
3000, el colector responde `NAK`.

### Clean Architecture
Estilo de arquitectura donde las capas se separan por responsabilidad y
las dependencias apuntan hacia adentro. Domain en el centro,
Infrastructure y otras capas externas. Ver `arquitectura.md`.

### CR-overwrite
Layout de impresora dot-matrix donde una línea lógica se compone de
varios segmentos separados por `\r`, cada uno escribiendo desde columna 0,
y el último carácter no-space gana. Usado por equipos OptiDist2 al
imprimir desde Windows.

### CTS / CancellationToken
Tipo .NET para propagar cancelación entre tareas async. Cuando el server
baja o el cliente cierra una conexión, se cancela el token y todas las
tareas linked terminan limpiamente.

### DataDir
Directorio donde el colector guarda toda su data: settings, plugins
override, instrumentos, samples, hotfolder. En Windows típicamente
`C:/ProgramData/PacCollector/`. En macOS `~/Library/Application Support/PacCollector/`.

### Dedup (deduplication)
Verificación que evita procesar dos veces el mismo sample. Se hace por la
terna `(serial, sampleIdentifier, startAt)`. Si ya existe un sample con
esa terna en el repo, se descarta el nuevo y se emite
`SampleDuplicateSkipped`.

### DI (Dependency Injection)
Patrón donde un objeto recibe sus dependencias por constructor en vez de
crearlas internamente. `Program.cs` registra las implementaciones, y los
constructors del Application y Infrastructure piden las interfaces.

### Distillation curve
Curva de destilación: serie de puntos `(% recuperado, temperatura)` que
representan cómo evoluciona la destilación de una muestra de combustible.
Para una gasolina típica: IBP ~35°C, 5% recovered ~55°C, 50% ~98°C,
FBP ~200°C.

### DTO (Data Transfer Object)
Objeto plano serializable a JSON. Existen para no exponer las entidades
de dominio directamente al cliente HTTP. Ver `application-use-cases.md`.

### EOL (End-of-line)
Tipo de fin de línea: `CR` (`\r`, viejos Mac), `LF` (`\n`, Unix), `CRLF`
(`\r\n`, Windows / HTTP / IPP), o `<none>` (sin EOL).

### FBP
Final Boiling Point. La temperatura más alta alcanzada durante la
destilación. Típicamente cerca del 95-98% recuperado.

### Fixture
En testing, un payload de ejemplo capturado de un equipo real. Vive en
`tests/PacCollector.ParityTests/Fixtures/*.bin`. Usado para tests de
paridad — el parseo debe producir el mismo `Sample` que en runs anteriores.

### Hotfolder
Carpeta destino del colector que un LIMS externo (el de YPF) vigila para
levantar archivos. Cada vez que el colector procesa un sample, escribe
un archivo en el hotfolder configurado. El LIMS los lee de forma
asíncrona.

### HP-GL
Hewlett Packard Graphics Language. Lenguaje de instrucciones gráficas
usado en impresoras HP. Los equipos PAC lo incluyen en sus print jobs para
dibujar la curva de destilación. El colector lo IGNORA del parseo (extrae
solo el texto).

### IBP
Initial Boiling Point. La primera gota de destilado. Punto inicial de la
curva.

### Idempotente
Una operación es idempotente si ejecutarla dos veces produce el mismo
resultado que ejecutarla una. `SampleProcessingService` es idempotente
gracias al dedup check.

### Interface (Port)
Contrato en C#. En Clean Architecture, las interfaces que vive en
Domain.Ports son los "ports" — definen QUÉ se puede hacer, las clases en
Infrastructure son los "adapters" que definen CÓMO.

### IPP
Internet Printing Protocol. Estándar IETF (RFC 8010 / 8011) para mandar
print jobs por red. Puerto TCP 631. Los equipos PAC modernos imprimen
vía IPP. El colector se hace pasar por una impresora HP LaserJet 4 para
maximizar compatibilidad.

### LIMS
Laboratory Information Management System. El software que YPF usa para
gestionar muestras, resultados, trazabilidad. No es PAC, no es este
colector. **Este colector es el puente** entre los equipos PAC y el LIMS
de YPF.

### LIMS Ethernet
Protocolo proprietary de PAC para mandar resultados al LIMS. UDP beacon
en puerto 3000 + ACK + TCP JSON en puerto del ACK (típicamente 9980).
Documentado en `docs/protocols/lims-ethernet.md`.

### NUL terminator
Byte `0x00`. En el protocolo LIMS Ethernet, marca el fin del JSON en la
sesión TCP. El TcpServer lee hasta encontrar este byte.

### OptiPMD, OptiDist2, OptiFZP, OptiCPP, etc.
Modelos de equipos PAC del laboratorio:
- `OptiPMD` — Mini-distillation (`ASTM D7345`).
- `OptiDist2` — Atmospheric distillation (`ASTM D86`).
- `OptiFZP` — Freeze point (`ASTM D7153`).
- `OptiCPP` — Cloud / pour point (`ASTM D2500`).
- `OptiFPP`, `OptiMVD`, `OptiMPP`, `OptiFuel` — otros analizadores PAC.

### PCL
Printer Command Language. Lenguaje de control de impresoras HP. Los
equipos PAC en modo Print mandan PCL + texto + HP-GL. `PclStripper` saca
las secuencias de escape para dejar solo el texto del reporte.

### PluginRegistry
Catálogo de los plugins activos en el colector. Permite buscar el plugin
correcto para un AnalyzerType (LIMS) o para un blob de bytes (print).

### Port (en Clean Architecture)
Una interfaz definida en Domain que Application usa para hablar con el
mundo exterior, sin saber su implementación. `ISampleRepository`,
`IFileWriter`, `IPluginRegistry`, etc. Son los "ports". Las clases en
Infrastructure son los "adapters" que implementan estos ports.

### Port (TCP/UDP)
Número de puerto de red. El colector usa:
- UDP 3000 — beacon LIMS Ethernet.
- TCP 9980 — sesión LIMS Ethernet (puerto típico, configurable).
- TCP 631 — print server IPP (puerto IPP estándar, configurable).
- TCP 5174 — API HTTP del colector (localhost).

### Recovery / Residue
- **Recovery (%)**: cuánto del volumen original se recuperó destilando.
  Típicamente >95%.
- **Residue (%)**: cuánto quedó en el balón sin destilar. Típicamente
  <2%.

### REST API
Estilo de API HTTP basado en recursos. El colector expone una REST API
en `localhost:5174/api/*`. Ver `api-endpoints.md`.

### Sample
Una muestra analizada por un equipo. Es el objeto central del dominio.
Tiene serial, sample identifier, fechas, IBP, FBP, curva, etc.
Ver `domain.md`.

### Serial (AnalyzerSerial)
Número de serie del equipo. Es el identificador único. Ej: `1216`,
`8076`, `215003`.

### Spec (plugin spec)
JSON declarativo que describe cómo parsear los datos de un equipo. Vive
en `DataDir/plugins/<lims|print>/{id}.json`. Permite agregar equipos
nuevos sin código.

### Template (hotfolder template)
JSON declarativo que describe el formato del archivo del hotfolder. Vive
en `DataDir/hotfolder-templates/{name}.json`. Permite formatos custom
de output sin código.

### UEL
Universal End-of-Language. Secuencia PCL `ESC %-12345X` que marca fin de
print job en raw PCL. El PrintServer cierra una conexión raw cuando ve
2 UELs consecutivos.

### UTF-8 strict
Modo de decodificación UTF-8 que tira excepción si los bytes son
inválidos, en vez de reemplazarlos con `?` o U+FFFD. Usado por
`PacFamilyPlugin` (JSON) para detectar payloads corruptos temprano.

### UUID
Identificador universal de 128 bits. El colector genera un UUID nuevo para
cada `Sample` recibido. Usado como clave en `InMemorySampleRepository` y
en los nombres de archivos `_unknown/`.

### WebSocket
Conexión TCP persistente que permite comunicación bidireccional en
tiempo real entre el server y el browser. La UI React mantiene una
conexión WebSocket a `/api/events` para recibir eventos a medida que
ocurren (samples nuevos, instrumentos descubiertos, errores).

### Write-through + fsync
Patrón de escritura atómica:
1. Abrir archivo con `FileOptions.WriteThrough`.
2. Escribir bytes.
3. `Flush()` para vaciar el buffer del FileStream.
4. `Flush(flushToDisk: true)` para forzar fsync al disco físico.
5. `Move` desde el tmp al destino final.

Garantiza que un power-loss no deje archivos a la mitad. Usado en
`AtomicWriter`, `ConfigStore`, `JsonInstrumentRepository`.
