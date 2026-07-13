# PAC Collector

Aplicación de captura de datos de instrumentos PAC del laboratorio (OptiPMD, OptiCPP, OptiFZP, OptiFPP, OptiDist, OptiDist2, OptiMVD, etc.).

Escucha a los equipos por red (UDP/TCP), parsea sus mensajes, los persiste en archivos legibles para LIMS y expone una interfaz web local para el operador.

Repo monorepo con backend + frontend juntos, entregable como un único paquete.

---

## Estructura del repo

```
pac-collector/
├── backend/                       ← Servidor .NET 10 (lógica, red, persistencia, plugins)
│   ├── src/                       ← 7 proyectos .NET (Domain, Application, Infrastructure, Api, Shell, MockDevice, Tools)
│   ├── tests/                     ← xUnit + tests de paridad + smoke tests
│   ├── docs/                      ← Documentación de protocolos de red (LIMS Ethernet, IPP)
│   ├── documentacion/             ← Documentación pedagógica en español
│   ├── scripts/                   ← build-installer.ps1 (Velopack)
│   ├── branding/                  ← Ícono multi-resolución
│   └── frontend/                  ← Acá va el dist/ del frontend compilado (se popula al buildear)
│
├── frontend/                      ← Interfaz web en React + Vite + TypeScript
│   ├── src/                       ← Componentes, vistas, cliente HTTP/WebSocket
│   ├── public/                    ← Assets estáticos
│   ├── e2e/                       ← Tests end-to-end con Playwright
│   └── package.json
│
└── README.md                      ← Este archivo
```

---

## Prerrequisitos

| Herramienta | Versión | Para qué |
|---|---|---|
| **.NET SDK** | 10.0 o superior | Compilar el backend |
| **Node.js** | 22.x o superior | Compilar el frontend |
| **pnpm** | 10.x o superior | Gestor de paquetes del frontend (`npm install -g pnpm`) |
| **PowerShell 7+** | opcional | Solo para armar el instalador `.exe` |

---

## Compilar y correr — paso a paso

### 1. Compilar el frontend

```bash
cd frontend
pnpm install                # baja dependencias (~1 min la primera vez)
pnpm build                  # genera dist/ con HTML+JS+CSS estáticos
```

### 2. Copiar el frontend compilado al backend

```bash
# desde la raíz del repo
mkdir -p backend/frontend/dist
cp -r frontend/dist/* backend/frontend/dist/
```

### 3. Compilar y correr el backend

```bash
cd backend
dotnet restore              # baja dependencias .NET
dotnet build                # compila todo
dotnet test                 # corre los tests (~150 tests, ~30 s)

# para correr localmente:
dotnet run --project src/PacCollector.Api
```

El backend levanta un servidor local en `http://127.0.0.1:5174`. Abrí esa URL en cualquier browser (Edge, Chrome) y ya ves la UI.

### 4. (Opcional) Generar el `.exe` instalador para Windows

Solo desde una PC Windows con PowerShell 7:

```powershell
cd backend
pwsh ./scripts/build-installer.ps1 -Version 1.0.0
```

Sale `backend/releases/PacCollectorSetup.exe` (~80 MB) — instalador self-contained que se distribuye e instala en cualquier PC Windows 10/11 sin prerrequisitos.

---

## Arquitectura general

**Dos ejecutables**, empaquetados en un único instalador:

| Ejecutable | Rol |
|---|---|
| `PacCollector.Api.exe` | El servidor. Escucha a los equipos PAC por red y expone la API HTTP local. |
| `PacCollector.Shell.exe` | Launcher liviano. Chequea si el Api corre, si no lo arranca, y abre el browser default del sistema en la URL local. |

**Flujo cuando el operador hace doble click**:

1. `Shell.exe` arranca → chequea `GET http://127.0.0.1:5174/api/health`.
2. Si no responde, lanza `Api.exe` como proceso hijo.
3. Abre el browser default en la URL local.
4. `Shell.exe` termina. El `Api.exe` sigue corriendo en background.

El operador interactúa con la UI desde el browser del sistema. No hay ventana embebida (WebView), lo que simplifica el mantenimiento y evita problemas de rendering en algunas PCs Windows.

---

## Protocolos que hablan los equipos

Documentados en `backend/docs/protocols/`:

- [`lims-ethernet.md`](backend/docs/protocols/lims-ethernet.md) — Protocolo nativo de los equipos PAC (UDP beacon + TCP JSON).
- [`print-ipp.md`](backend/docs/protocols/print-ipp.md) — Modo "impresora de red" (IPP / PCL) para equipos legacy.

---

## Documentación en español para el equipo de mantenimiento

En `backend/documentacion/` hay 16 archivos `.md` que explican pedagógicamente cada capa del backend:

- `arquitectura.md` — Vista general
- `domain.md`, `application-services.md`, `application-use-cases.md`
- `infrastructure-network.md`, `infrastructure-persistence.md`, `infrastructure-plugins.md`, `infrastructure-filesystem.md`, `infrastructure-hotfolder.md`, `infrastructure-config.md`
- `api-endpoints.md` — Listado y descripción de todos los endpoints REST + WebSocket
- `ejecutable-shell.md`, `ejecutable-pac-mock.md`, `ejecutable-pac-tool.md`
- `glosario.md` — Términos técnicos explicados

Los archivos `.cs` principales del backend también tienen comentarios inline en español marcando de dónde viene cada palabra (`[C#]` / `[.NET]` / `[NUESTRO]`).

---

## Testing

```bash
# backend
cd backend && dotnet test

# frontend (unit tests)
cd frontend && pnpm test

# frontend (end-to-end, requiere backend corriendo)
cd frontend && pnpm exec playwright test
```

---

## Persistencia (dónde vive el estado)

En Windows, todo lo persistente vive en `%PROGRAMDATA%\PacCollector\`:

| Carpeta / archivo | Qué contiene |
|---|---|
| `settings.json` | Configuración global (puertos, IPs, formatos de salida, hotfolder routes) |
| `instruments.json` | Tabla de equipos detectados (con alias, IP última vista, contador de samples) |
| `db/<serial>/` | Archivos por equipo (JSON, TXT, CSV, curvas) |
| `db/master.csv` | Master CSV global de todos los ensayos |
| `plugins/lims/*.json` | Plugins de equipo (mode LIMS) — override en runtime |
| `plugins/print/*.json` | Plugins de equipo (mode Print) — override en runtime |
| `hotfolder-templates/*.json` | Templates de output para LIMS externos |

---

## Versiones de referencia

Probado y validado con:

| Componente | Versión |
|---|---|
| .NET SDK | 10.0.x |
| Node.js | 22.14.0 |
| pnpm | 10.31.0 |
| Windows | 10 / 11 (x64) |
| macOS | 15+ (solo para desarrollo del frontend) |

---

## Soporte de mantenimiento

Todo el código usa librerías estándar y ampliamente soportadas:

- **Backend**: .NET LTS de Microsoft (`System.Text.Json`, ASP.NET Core, `System.Threading.Channels`). Sin ORMs (`EF Core`), sin `AutoMapper`, sin `MediatR`, sin `Newtonsoft`. Stack minimalista, mantenible por cualquier dev C# senior.
- **Frontend**: React + Vite + TypeScript. Sin frameworks propietarios. Cualquier dev frontend puede tocarlo.

Ver los `README.md` de cada subcarpeta para detalles específicos.
