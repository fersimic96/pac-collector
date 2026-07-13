# Application — Use Cases y DTOs

Path: `src/PacCollector.Application/`

Esta capa contiene los **casos de uso del negocio**: un archivo por verbo
("recibir muestra", "listar instrumentos", "actualizar alias"). Cada use case
es chico y enfocado en una sola operación.

Solo depende de `Domain`. No sabe que existen archivos, sockets, JSON o
HTTP. Recibe sus dependencias (interfaces de `Domain.Ports`) por constructor.

---

## Use Cases

### ReceiveSampleUseCase.cs
Path: `src/PacCollector.Application/UseCases/ReceiveSampleUseCase.cs`

Entrada para datos que llegan por **LIMS Ethernet** (UDP beacon + ACK + TCP
JSON). Es el use case que invoca el `TcpServer` cuando un equipo le manda
un JSON.

**Dependencias**: `SampleProcessingService`.

#### `ExecuteAsync(raw, sourceIp, ct) → PacChecksum`

1. Calcula el `PacChecksum` de los bytes recibidos (el checksum que el
   equipo espera de vuelta).
2. Llama a `_processing.ProcessRawMessageAsync(raw, sourceIp, ct)` — toda la
   pipeline grande de parseo/persistencia/output vive ahí.
3. Devuelve el checksum.

**Decisión de diseño**: el checksum se calcula ACÁ y se devuelve al caller,
para que el TcpServer pueda mandar `{"Error":"","SaveCheckSum":"00A3"}` de
vuelta al equipo sin necesidad de saber qué hace el processing service por
debajo.

---

### ReceivePrintUseCase.cs
Path: `src/PacCollector.Application/UseCases/ReceivePrintUseCase.cs`

Entrada para datos que llegan por **Print over Ethernet** (TCP 631, IPP o
raw PCL). Invocado por `PrintServer` cuando termina de drenar una conexión.

**Dependencias**: `SampleProcessingService`.

#### `ExecuteAsync(raw, sourceIp, ct) → Task`

Pasa los bytes a `_processing.ProcessPrintMessageAsync(raw, sourceIp, ct)`.
A diferencia de `ReceiveSampleUseCase`, no devuelve checksum porque el modo
print no espera respuesta del colector (el equipo ya cerró la conexión).

---

### ListSamplesUseCase.cs
Path: `src/PacCollector.Application/UseCases/ListSamplesUseCase.cs`

Listado paginado de samples para mostrar en la UI o exportar.

**Dependencias**: `ISampleRepository`.

#### `ExecuteAsync(filters, offset, limit, ct) → SamplePage`

1. Convierte los filtros de input (`SampleFiltersInput`) a filtros de dominio
   (`SampleQueryFilters`).
2. Llama a `_samples.ListPaginatedAsync(filters, offset, limit, ct)` para
   traer la página.
3. Llama a `_samples.CountAsync(filters, ct)` para el total.
4. Mapea las entidades Sample a `SampleOutputDto`.
5. Devuelve un `SamplePage(items, total, offset, limit)`.

---

### ListInstrumentsUseCase.cs
Path: `src/PacCollector.Application/UseCases/ListInstrumentsUseCase.cs`

Listado de todos los instrumentos descubiertos.

**Dependencias**: `IInstrumentRepository`.

#### `ExecuteAsync(ct) → IReadOnlyList<InstrumentOutputDto>`

Llama a `_instruments.ListAllAsync(ct)` y mapea cada entidad `Instrument` a
`InstrumentOutputDto`.

---

### HandleBeaconUseCase.cs
Path: `src/PacCollector.Application/UseCases/HandleBeaconUseCase.cs`

Procesa la recepción de un beacon UDP del equipo PAC. Es muy chiquito —
solo emite un evento para que la UI lo vea en tiempo real (útil para
debug y observabilidad).

**Dependencias**: `IEventBus`.

#### `Execute(ip) → void`

Emite un evento `DomainEvent.BeaconReceived(ip, now)`. **No persiste nada
ni responde al beacon** — esa parte la hace el `UdpServer` directamente
(manda el ACK). Este use case es solo el "registro" de que pasó.

---

### UpdateInstrumentAliasUseCase.cs
Path: `src/PacCollector.Application/UseCases/UpdateInstrumentAliasUseCase.cs`

Cambia el alias humano de un instrumento. Llamado desde el endpoint
`PATCH /api/instruments/{serial}/alias`.

**Dependencias**: `IInstrumentRepository`.

#### `ExecuteAsync(serial, alias, ct) → Task`

1. Si el `alias` no es null, hace trim. Si queda vacío, lo trata como null
   (= sacar el alias).
2. Llama a `_instruments.UpdateAliasAsync(serial, cleaned, ct)`.

---

## DTOs (Data Transfer Objects)

Los DTOs convierten **entidades de dominio** en objetos planos serializables
a JSON. Existen para no exponer la entidad directamente al cliente HTTP —
así el modelo de dominio puede evolucionar sin romper el contrato API.

### SampleOutputDto.cs
Path: `src/PacCollector.Application/Dtos/SampleOutputDto.cs`

Record con todos los campos de Sample en forma serializable + un sub-record
`CurvePointDto` para la curva.

#### `FromEntity(sample) → SampleOutputDto` (static)
Mapea un `Sample` a su DTO. Convierte `AnalyzerSerial` a string, el
`DistillationCurve` a lista de `CurvePointDto`, el `SortedDictionary` a
`Dictionary` plano.

---

### InstrumentOutputDto.cs
Path: `src/PacCollector.Application/Dtos/InstrumentOutputDto.cs`

Record con los campos de un instrumento para exponer en API. Tiene `FromEntity`
similar.

---

### SampleFiltersInput.cs
Path: `src/PacCollector.Application/Dtos/SampleFiltersInput.cs`

Record con los filtros que el cliente puede mandar al listar samples:
`Serial`, `Program`, `Operator`, `From`, `To`. Todos opcionales.

---

### SamplePage.cs
Path: `src/PacCollector.Application/Dtos/SamplePage.cs`

Record que envuelve una página de resultados:
`SamplePage(Items, Total, Offset, Limit)`. El cliente sabe si tiene más
páginas comparando `Offset + Items.Count` contra `Total`.
