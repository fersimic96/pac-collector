# Ejecutable — PacCollector.Shell

Path: `src/PacCollector.Shell/`

Binario: `PacCollector.Shell.exe`. Es un **launcher liviano** del Api +
abridor del browser. No es un wrapper WebView.

---

## Cómo funciona

El `Shell` no contiene UI ni WebView. Es un proceso corto que:

1. Chequea si `PacCollector.Api.exe` ya está corriendo (pingea
   `GET http://127.0.0.1:5174/api/health`).
2. Si no, lo arranca como subproceso (o vía Windows Service Manager si está
   instalado como service).
3. Espera a que `/api/health` responda con `200 OK` (timeout 15s).
4. Abre el browser default del usuario con `http://127.0.0.1:5174`.
5. Termina (exit 0). El Api sigue corriendo en background.

Esto reemplazó la idea original de usar Photino + WebView2 que daba pantalla
en blanco en algunas PCs de YPF (paths con espacios, cache de WebView2
flaky, ProgramData permisos). Usar el browser nativo es 100% confiable.

**El Api persiste en background**. Cerrar la pestaña del browser NO mata el
Api. Para apagarlo: Administrador de tareas → finalizar `PacCollector.Api.exe`.

---

## Program.cs
Path: `src/PacCollector.Shell/Program.cs`

### Variables clave

| Variable | Valor | Override |
|---|---|---|
| `apiUrl` | `http://127.0.0.1:5174` | env var `PAC_SHELL_URL` |
| `ServiceName` | `"PacCollector"` | hardcoded |
| `logPath` | `%LocalAppData%/PacCollector/logs/shell-{timestamp}.log` | hardcoded |

### Flujo en Program.cs

1. `VelopackApp.Build().Run()` — hook de Velopack para manejar install/
   uninstall/update hooks. Si la app NO está instalada via Velopack, es no-op.
2. Logging a archivo (winexe sin consola).
3. `ApiReadyAsync(800ms)` — chequea si el Api ya está vivo.
4. Si NO está corriendo:
   - Si Windows + service instalado → `ServiceController.TryStart(ServiceName)`.
   - Else → `ApiLauncher.SpawnSibling()` (arranca el binario hermano).
5. `WaitForApiAsync(15s)` — poll hasta que `/api/health` responda.
6. `Process.Start(apiUrl, UseShellExecute=true)` — Windows abre el browser
   default registrado para http://.
7. `return 0`.

### Funciones helper

#### `ApiReadyAsync(timeout) → bool` (async lambda)
GET a `apiUrl + "/api/health"` con timeout. True si responde 2xx.

#### `WaitForApiAsync(timeout) → bool` (async lambda)
Loop con polling cada 250ms hasta deadline o hasta que ApiReadyAsync diga
true.

---

## ApiLauncher.cs
Path: `src/PacCollector.Shell/ApiLauncher.cs`

### `SpawnSibling() → void` (static)
Arranca `PacCollector.Api.exe` que vive al lado del Shell. Lo hace como
subproceso desacoplado:
- `ProcessStartInfo` con UseShellExecute false.
- `CreateNoWindow = true` (no consola).
- El proceso hijo sobrevive aunque el Shell termine.

Detalle: usa `Path.GetDirectoryName(typeof(Program).Assembly.Location)`
para encontrar el ejecutable hermano. Eso funciona tanto en deploy
Velopack (todos los exes en la misma carpeta) como en dev (bin/Release).

---

## ServiceController.cs
Path: `src/PacCollector.Shell/ServiceController.cs`

Wrapper sobre la SCM (Service Control Manager) de Windows.

### `IsServiceInstalled(name) → bool` (static)
Devuelve true si existe un Windows Service con ese nombre.

### `TryStart(name) → bool` (static)
Intenta arrancar el service via `ServiceController.Start()`. Devuelve true
si el arranque tuvo éxito o el service ya estaba running.

Estos métodos solo se llaman si `OperatingSystem.IsWindows()`. En Linux/Mac
el Shell siempre va por el path `SpawnSibling`.

---

## Cuándo usar Shell vs el Api directo

- **Use Shell** para deploys de escritorio en YPF. El operador hace doble
  click en un icono, se abre la "app". Velopack puede instalarlo con un
  `.exe` instaler que crea atajos en menú inicio.

- **Use el Api directo (sin Shell)** para correr el colector como Windows
  Service en un host headless (sin operador local). En ese caso la UI se
  accede desde otra PC navegando a `http://hostname:5174`.

Las dos opciones usan **el mismo backend**. La diferencia es solo cómo se
arranca y cómo se accede a la UI.
