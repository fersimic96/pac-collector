# Infrastructure — Network

Path: `src/PacCollector.Infrastructure/Network/`

Listeners de red que reciben datos de los equipos PAC. Hay **tres servers**
(UDP, TCP LIMS, TCP Print) más un orquestador (`ListenerManager`) y helpers
de protocolo.

---

## Protocol.cs
Path: `src/PacCollector.Infrastructure/Network/Protocol.cs`

**Constantes del protocolo proprietary PAC.** Centralizadas para que si el
fabricante cambia algo, se toque un solo lugar.

| Constante | Valor | Uso |
|---|---|---|
| `Beacon` | `[0x01, 0x02, 0x03]` | Lo que el equipo manda por UDP al descubrir. |
| `NullTerminator` | `0x00` | Terminator del JSON en la sesión TCP. |
| `Nak` | `"NAK"` (bytes UTF-8) | Lo que el server responde por UDP a un packet desconocido. |
| `TcpReadChunk` | 1024 | Tamaño del buffer de lectura TCP. |
| `TcpReadTimeoutMs` | 5000 | Timeout por read TCP. |

### `BuildAck(serverIp, tcpPort) → string`
Construye el ACK: `"ACK 192.168.100.50 9980"`. Tres tokens separados por
espacio. Sin CR/LF, sin null terminator. Lo manda el `UdpServer` cuando
reconoce un beacon.

### TcpLimsResponse (clase)
Representa la respuesta JSON `{Error, SaveCheckSum}` que el TcpServer manda
al equipo después de procesar.

#### `Ok(checksum) → TcpLimsResponse` (static)
Construye respuesta exitosa: `Error = ""`, `SaveCheckSum = checksum`.

#### `Nack(checksum) → TcpLimsResponse` (static)
Construye respuesta de error: `Error = "NACK"`, `SaveCheckSum = checksum`.
El equipo, al recibir esto, sabe que algo falló downstream.

#### `ToCompactJson() → string`
Serializa el record a JSON compacto (sin indent).

---

## UdpServer.cs
Path: `src/PacCollector.Infrastructure/Network/UdpServer.cs`

Escucha el **beacon UDP del equipo en el puerto 3000** y responde con el
ACK que le dice al equipo dónde abrir la conexión TCP.

### Constructor
```csharp
UdpServer(IPEndPoint bindAddr, string? configuredIp, ushort tcpPort,
          HandleBeaconUseCase handleBeacon, Action<string>? log)
```

- `bindAddr`: típicamente `0.0.0.0:3000`.
- `configuredIp`: si está seteado, esa IP va en el ACK. Si null, se
  autodetecta.
- `tcpPort`: el puerto que va en el ACK (donde escucha TcpServer, típicamente
  9980).
- `handleBeacon`: use case que registra el evento de beacon recibido.
- `log`: callback opcional para logging.

### `RunAsync(ct) → Task`

Loop infinito hasta cancellation:

1. Crea `UdpClient(_bindAddr)`. Habilita broadcast.
2. Loop:
   - `udp.ReceiveAsync(ct)` espera un packet.
   - Si los bytes son `0x01 0x02 0x03` (beacon):
     - Determina la IP local (`_configuredIp` o autodetect).
     - Construye el ACK: `BuildAck(localIp, tcpPort)`.
     - Manda el ACK por UDP unicast al remoto.
     - Llama a `_handleBeacon.Execute(remote.Address)` para emitir el evento.
   - Si los bytes son cualquier otra cosa:
     - Manda `"NAK"` por UDP unicast.
     - Loggea el packet desconocido (`hex` preview).
3. Errores transient (excepto OOM y cancellation): se loggean y se continúa.

### `IsBeacon(data) → bool` (static private)
Compara los bytes recibidos contra `Protocol.Beacon` exactamente (length +
content via `SequenceEqual`).

### `LocalIpForRemote(remote) → IPAddress?` (static private)
**Autodetect de la IP local que ve el remoto.** Abre un socket UDP "connected"
(sin mandar nada), lee el `LocalEndPoint` que le asignó el sistema, devuelve
esa IP. Si falla, null.

> Esto es importante porque el ACK le dice al equipo "abrime TCP en
> ESTA ip". La IP correcta depende desde qué interfaz se escucha — si el
> server tiene varias NICs, una local y una para el VLAN del lab, hay que
> mandar la que el equipo PAC puede alcanzar. El autodetect lo hace bien
> para casos simples; para configuraciones complejas se setea
> `general.selectedIp` en la config.

---

## TcpServer.cs
Path: `src/PacCollector.Infrastructure/Network/TcpServer.cs`

Escucha conexiones **TCP en el puerto 9980** (típicamente). Los equipos
PAC le mandan el JSON con los resultados del ensayo terminados en NUL byte,
y esperan una respuesta JSON `{Error, SaveCheckSum}` antes de cerrar.

### Constructor
```csharp
TcpServer(IPEndPoint bindAddr, ReceiveSampleUseCase receiveSample, Action<string>? log)
```

### `RunAsync(ct) → Task`

Accept loop clásico:

1. `TcpListener(_bindAddr).Start()`.
2. Loop:
   - `listener.AcceptTcpClientAsync(ct)` espera una conexión.
   - Lanza `HandleConnectionAsync(client, ct)` en task fire-and-forget para
     no bloquear el accept con el procesamiento de un cliente.
3. Al cancelar, `listener.Stop()`.

### `HandleConnectionAsync(client, ct) → Task` (private)

Procesa **una conexión TCP**:

1. Lee bytes con `ReadUntilNullAsync` (lee hasta encontrar `0x00` o cerrar).
2. Llama a `_receiveSample.ExecuteAsync(buf, remoteIp, ct)` para procesar.
3. Si fue OK, construye `TcpLimsResponse.Ok(checksum)`.
4. Si falló: igual computa el checksum del raw + construye
   `TcpLimsResponse.Nack(checksum)`. **Siempre devuelve un checksum**, así
   el equipo no se queda esperando.
5. Serializa la respuesta a JSON compacto + UTF-8.
6. `stream.WriteAsync(bytes, ct)`.
7. `Shutdown(Both)` para cerrar.

### `ReadUntilNullAsync(stream, ct) → ReadOnlyMemory<byte>` (static private)

Lee chunks de 1024 bytes hasta encontrar `0x00` o que la conexión cierre.
Timeout per-read de 5s para evitar conexiones colgadas. Devuelve los bytes
ANTES del NUL terminator (el NUL no se incluye en el payload).

---

## PrintServer.cs
Path: `src/PacCollector.Infrastructure/Network/PrintServer.cs`

Escucha **TCP en el puerto 631** (IPP estándar, configurable). Acepta dos
formatos:

- **HTTP/IPP** (RFC 8010): equipos modernos y PCs con CUPS que imprimen
  via `Generic Printer`.
- **Raw PCL/HP-GL**: equipos PAC viejos (línea IRIS) que envían el print job
  sin envoltura HTTP.

El server **clasifica automáticamente** mirando los primeros bytes y rutea
al handler correspondiente.

### Constructor
```csharp
PrintServer(IPEndPoint bindAddr, ReceivePrintUseCase receivePrint, Action<string>? log)
```

### `RunAsync(ct) → Task`
Accept loop como TcpServer. Despacha cada conexión a
`HandleConnectionAsync`.

### `HandleConnectionAsync(client, ct) → Task` (private)

1. `SniffAsync(stream, ct)` — lee hasta 32 bytes para clasificar.
2. `PrintClassifier.Classify(sniff)` devuelve `Http`, `Raw` o `Indeterminate`.
3. Rutea:
   - HTTP → `HandleIppAsync(stream, remoteIp, sniff, ct)`.
   - Raw o Indeterminate → `HandleRawAsync(stream, remoteIp, sniff, ct)`.

### `SniffAsync(stream, ct) → byte[]` (private static)

Lee hasta 32 bytes con max 8 reads cortos (300ms cada uno). Se detiene
apenas la clasificación deja de ser `Indeterminate`.

### `HandleRawAsync(stream, remoteIp, head, ct) → Task` (private)

Para conexiones que NO son HTTP. Acumula bytes hasta:
- Encontrar 2 markers UEL (`ESC %-12345X`) que indican fin de job.
- O un gap de inactividad de 800ms.
- O hit a max bytes (4 MB hardcoded).

Después llama a `_receivePrint.ExecuteAsync(buf, remoteIp, ct)` con el blob.

### `HandleIppAsync(stream, remoteIp, head, ct) → Task` (private)

Para conexiones HTTP/IPP:

1. Lee headers HTTP hasta `\r\n\r\n`.
2. Parsea `Content-Length` de los headers.
3. Lee el body completo (ese tamaño).
4. Extrae el `request_id` (4 bytes en posiciones 4..7 del body IPP).
5. Construye respuesta IPP "successful-ok" usando `IppResponseBuilder.BuildOk`.
6. Manda la respuesta + cierra.
7. Procesa el body como print payload llamando a `_receivePrint.ExecuteAsync`.

---

## PrintClassifier.cs
Path: `src/PacCollector.Infrastructure/Network/PrintClassifier.cs`

Clasifica una conexión entrante al puerto 631.

### `Classify(head) → PrintClassification` (static)

Devuelve:
- `Http` — si empieza con un método HTTP conocido (`POST `, `GET `, etc.).
- `Indeterminate` — si los primeros 8 bytes son ASCII uppercase o espacio
  (podría ser HTTP truncado, hay que esperar más bytes).
- `Raw` — cualquier otra cosa.

### PrintClassification (enum)
`Http`, `Raw`, `Indeterminate`.

---

## IppResponseBuilder.cs
Path: `src/PacCollector.Infrastructure/Network/IppResponseBuilder.cs`

Construye respuestas IPP "successful-ok" para que los equipos PAC crean que
imprimieron OK.

### `BuildOk(requestId) → byte[]` (static)

Arma una respuesta IPP/1.1 + HTTP wrapping:

- IPP body: versión 1.1, status-code `0x0000` (successful-ok), echo del
  `requestId` del request, attributes-charset `utf-8`, operation/printer
  attributes mínimos (`printer-uri-supported`, `printer-name` =
  `"PAC-IRIS-CAPTURE"`, `printer-make-and-model` = `"HP LaserJet 4"`, etc.).
- HTTP wrapper: `HTTP/1.1 200 OK`, `Content-Type: application/ipp`,
  `Content-Length`, `Connection: close`.

**Por qué `HP LaserJet 4`**: muchos drivers de impresora de equipos PAC
asumen impresoras HP de esa línea por compatibilidad. Decir que somos eso
maximiza chances de que el equipo acepte la conexión.

---

## ListenerManager.cs
Path: `src/PacCollector.Infrastructure/Network/ListenerManager.cs`

**Orquesta el ciclo de vida de los 3 servers** (UDP + TCP LIMS + Print).
Permite arrancarlos y pararlos individualmente desde la UI.

### Constantes
- `UdpPort = 3000`
- `TcpPort = 9980`

### Propiedades

- `LimsRunning → bool` — true si UDP o TCP LIMS están corriendo.
- `PrintRunning → bool` — true si el Print server está corriendo.

### `StartLims() → void`

Bajo lock, si LIMS no está corriendo:
1. Crea `CancellationTokenSource`.
2. Lee config para `SelectedIp`.
3. Instancia `UdpServer` y `TcpServer` con sus bind addresses.
4. Los lanza con `Task.Run(...)` y guarda los tasks.

### `StopLims() → void`

Bajo lock, captura los tasks. Fuera del lock cancela el CTS y espera a que
los tasks terminen.

### `StartPrint() → void`

Similar a `StartLims` pero con `PrintServer` en el puerto que dice la config
(`general.printPort`, default 631).

### `StopPrint() → void`
Stop simétrico de Print.

### `StopAll() → void`
StopLims + StopPrint.

**Decisión de diseño**: separar LIMS de Print así el operador puede tener
solo uno habilitado si la lab no usa el otro modo. Y los endpoints
`/api/listeners/start` / `/api/listeners/stop` exponen este control.
