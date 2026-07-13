# API HTTP — Endpoints

Path: `src/PacCollector.Api/Endpoints/`

El `Api` expone una API REST + WebSocket en `http://127.0.0.1:5174`. Los
endpoints están agrupados por dominio en 9 archivos. Cada archivo registra
sus rutas con `MapXxxEndpoints` que se llaman desde `Program.cs`.

Todos los endpoints serializan en **camelCase** y los enums se mandan como
strings en **snake_case** (matchea el frontend React).

---

## HealthEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/HealthEndpoints.cs`

### `GET /api/health`
Healthcheck simple. Devuelve `200 OK` con `{ "status": "ok" }`. Lo usa
cualquier sistema de monitoring (Nagios, Prometheus blackbox, k8s probes)
para saber si el server está vivo.

---

## SampleEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/SampleEndpoints.cs`

### `GET /api/samples/{uuid}`
Devuelve un sample específico por UUID. `404` si no existe.

Response: `SampleOutputDto` con todos los campos del sample incluida la
curva.

### `POST /api/samples/search`
Body:
```json
{
  "serial": "1216",
  "program": "ASTM D7345",
  "operator": "Fer",
  "from": "2026-05-01T00:00:00Z",
  "to": "2026-06-01T00:00:00Z",
  "offset": 0,
  "limit": 50
}
```
Todos los filtros son opcionales. Devuelve un `SamplePage(items, total,
offset, limit)`.

---

## InstrumentEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/InstrumentEndpoints.cs`

### `GET /api/instruments`
Lista todos los instrumentos descubiertos. Response: array de
`InstrumentOutputDto`.

### `PATCH /api/instruments/{serial}/alias`
Body:
```json
{ "alias": "PMD-LAB-A" }
```
Cambia el alias del instrumento. `404` si el serial no existe.

### `PATCH /api/instruments/{serial}/route`
Body:
```json
{ "hotFolderFormat": "CsvAll", "hotFolderDir": "C:/LIMS/hot/", "alias": "LAB-A" }
```
Configura una `InstrumentRoute` para este serial. `204 No Content`.

---

## ListenerEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/ListenerEndpoints.cs`

### `GET /api/server/status`
**Endpoint clave para la UI** — el TopBar.tsx del React lee este.

Devuelve `ServerStatusResponse`:
```json
{
  "serverIp": "auto",
  "tcpPort": 9980,
  "udpPort": 3000,
  "instrumentsCount": 3,
  "samplesToday": 47,
  "running": true,
  "printRunning": false,
  "printPort": 631
}
```

### `POST /api/listeners/start`
Arranca UDP + TCP listeners (LIMS Ethernet).

### `POST /api/listeners/stop`
Para los listeners LIMS.

### `POST /api/print-listener/start`
Arranca el listener Print (TCP 631 o el puerto configurado).

### `POST /api/print-listener/stop`
Para el listener Print.

---

## ConfigEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/ConfigEndpoints.cs`

### `GET /api/config`
Devuelve el `AppConfig` actual (snapshot). Useful para que la UI cargue su
estado inicial.

### `POST /api/config`
Body: `AppConfig` completo. Valida (`cfg.Validate()`); si hay errores
devuelve `400` con `{ errors: [...] }`. Si OK, persiste con
`ConfigStore.Replace`.

---

## PluginEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/PluginEndpoints.cs`

### `GET /api/plugins`
Lista metadata de los plugins LIMS (los print se ocultan — son detalle
interno).

### `PATCH /api/plugins/{id}/enabled`
Body: `{ "enabled": true }`. Habilita o deshabilita un plugin sin sacarlo
del registry.

### `POST /api/plugins/lims`
Body: JSON spec de un plugin LIMS. Lo guarda en
`DataDir/plugins/lims/{id}.json` y dispara reload del registry.
- `201 Created` con info del path donde quedó si fue OK.
- `400` con detalle si validación falló.

### `POST /api/plugins/print`
Idéntico pero para print specs.

### `DELETE /api/plugins/lims/{id}`
Borra un plugin LIMS del override dir. **No puede borrar embedded** (los
built-in tienen prioridad). Reloads tras delete.

### `DELETE /api/plugins/print/{id}`
Idéntico para print.

### `POST /api/plugins/reload`
Releé los specs de los override dirs y refresca el registry. Útil para
recargar tras edición manual del JSON sin reiniciar el server.

---

## NetworkEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/NetworkEndpoints.cs`

### `GET /api/network/local-ips`
Lista las IPs locales del host. Para que el operador elija desde la UI cuál
poner en `SelectedIp` (la IP que va en el ACK UDP).

### `GET /api/network/interfaces`
Lista las interfaces de red con su info (nombre, IPs, MAC).

---

## SystemEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/SystemEndpoints.cs`

### `GET /api/system/platform`
Devuelve `{ "platform": "windows" }` (o linux/macos). Para que la UI sepa
qué OS está debajo.

### `POST /api/system/open-network-settings`
Abre el panel de network del OS desde el server. Útil si la UI quiere un
botón "configurar red" que abre el control panel. `501` si el OS no lo
soporta.

---

## WebSocketEndpoints.cs
Path: `src/PacCollector.Api/Endpoints/WebSocketEndpoints.cs`

### `GET /api/events` (upgrade a WebSocket)

Canal de eventos en tiempo real para la UI. El cliente abre un WebSocket
y recibe los eventos del dominio a medida que ocurren.

Formato:
```json
{ "type": "SampleReceived", "payload": { "uuid": "...", "serial": "1216", ... } }
{ "type": "InstrumentDiscovered", "payload": { "serial": "9999", "analyzerType": "OptiFZP", "ip": "192.168.1.50" } }
```

El `type` es el nombre del record concreto (heredan de `DomainEvent`). El
`payload` es el record completo. La UI usa el `type` para discriminar y
actualizar la pantalla correspondiente (dashboard, lista de instrumentos,
logs, etc.).

#### Flujo
1. Cliente abre WebSocket.
2. Server hace `bus.Subscribe(out var reader)` — obtiene un reader del
   channel.
3. Server itera `reader.ReadAllAsync(ct)` y manda cada evento serializado.
4. Cuando el cliente cierra o el server baja, se cancela el linked CTS y
   el loop termina.

---

## Cómo se mapean en Program.cs

En `Program.cs`:

```csharp
app.MapHealthEndpoints();
app.MapSampleEndpoints();
app.MapInstrumentEndpoints();
app.MapPluginEndpoints();
app.MapConfigEndpoints();
app.MapListenerEndpoints();
app.MapNetworkEndpoints();
app.MapSystemEndpoints();
app.MapWebSocketEndpoints();
```

Agregar un endpoint nuevo: crear `MyEndpoints.cs` con
`MapMyEndpoints(this IEndpointRouteBuilder app)`, agregar la línea en
`Program.cs`.

## DTOs de request/response

Los DTOs se definen como `record` en el mismo archivo del endpoint, encima
del `static class`. Ej: `SampleSearchRequest`, `UpdateAliasRequest`,
`ServerStatusResponse`, `SetRouteRequest`. Esto mantiene los contratos HTTP
visibles donde se consumen.

## Services auxiliares

`PacCollector.Api/Services/`:

- `NetworkInfoService` — listar IPs e interfaces del host.
- `SystemService` — detectar OS, abrir panel de network.
- `PluginUploadService` — guardar/borrar plugins en el override dir,
  validar al uploadear (compile + activate test), dispara reload del
  registry.
