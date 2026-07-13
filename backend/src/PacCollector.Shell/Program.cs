// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA — de dónde viene cada palabra (en los comentarios uso estas etiquetas)
// ══════════════════════════════════════════════════════════════════════════════
//   [C#]      = palabra reservada del lenguaje C# (aparece en color en el IDE, no se puede renombrar)
//   [.NET]    = clase o método de Microsoft (viene con el framework .NET)
//   [NUESTRO] = clase o método que escribimos nosotros (vive en este proyecto)
//   [EXTERNO] = biblioteca de terceros instalada (NuGet)
// ══════════════════════════════════════════════════════════════════════════════

// ─── IMPORTACIONES ───────────────────────────────────────────────────────────
// using [C#] = "quiero usar código de esta biblioteca"
// Sin esto, las clases de abajo no existen para este archivo.

using System.Diagnostics;   // [.NET]    namespace de Microsoft — trae Process, ProcessStartInfo
using PacCollector.Shell;   // [NUESTRO] nuestro código: ApiLauncher y ServiceController (misma carpeta)
using Velopack;             // [EXTERNO] biblioteca de terceros: instalación/actualización del .exe

// ─── VELOPACK (instalador) ───────────────────────────────────────────────────
// Velopack es el sistema de auto-update. Esta línea intercepta eventos del instalador
// (instalar, desinstalar, actualizar). Si la app NO está instalada via Velopack, no hace nada.
VelopackApp.Build().Run();
// └─ VelopackApp [EXTERNO] .  Build() [EXTERNO] método  .  Run() [EXTERNO] método

// ─── CONSTANTES ──────────────────────────────────────────────────────────────
// const [C#] = valor fijo que NUNCA cambia mientras el programa corre.
//              A diferencia de var, no se puede reasignar después.
//              Es igual al const de JavaScript.
// string [C#] = tipo de dato "texto"

const string DefaultApiUrl = "http://127.0.0.1:5174";
// └─ DefaultApiUrl [NUESTRO] nombre de variable. La URL donde escucha la Api.
//    127.0.0.1 = esta misma PC (loopback). Puerto 5174.

const string ServiceName = "PacCollector";
// └─ ServiceName [NUESTRO] nombre de variable. El nombre del Windows Service, si está instalado.

// ─── VARIABLE (puede cambiar) ─────────────────────────────────────────────────
// var [C#] = variable cuyo tipo el compilador deduce solo. Puede cambiar de valor.
// ?? [C#]  = operador "si es null, usá esto otro" (igual que en JavaScript)

var apiUrl = Environment.GetEnvironmentVariable("PAC_SHELL_URL") ?? DefaultApiUrl;
// └─ apiUrl [NUESTRO] variable  .  Environment [.NET] clase  .  GetEnvironmentVariable() [.NET] método
//    lee la variable de entorno PAC_SHELL_URL.
//    Si existe → usa esa URL (permite cambiar la URL sin recompilar).
//    Si NO existe (null) → usa DefaultApiUrl = "http://127.0.0.1:5174".

// ─── LOG A ARCHIVO ────────────────────────────────────────────────────────────
// Shell.exe no tiene consola visible (es winexe). Para debuggear, escribe un log en disco.

var logDir = Path.Combine(
    // Path [.NET] clase  .  Combine() [.NET] método — une partes de un path con el separador correcto del SO
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    // └─ Environment [.NET] clase  .  GetFolderPath() [.NET] método  .  SpecialFolder [.NET] enum
    //    devuelve C:\Users\<usuario>\AppData\Local (en Windows)
    "PacCollector",
    "logs");
// resultado: C:\Users\<usuario>\AppData\Local\PacCollector\logs

Directory.CreateDirectory(logDir);
// └─ Directory [.NET] clase  .  CreateDirectory() [.NET] método
//    crea esa carpeta si no existe. Si ya existe, no hace nada.

var logPath = Path.Combine(logDir, $"shell-{DateTime.Now:yyyyMMdd-HHmmss}.log");
// └─ DateTime [.NET] clase  .  Now [.NET] propiedad (fecha/hora actual)
//    arma el path del archivo de log con timestamp en el nombre → shell-20260706-143022.log
//    El $ adelante del string [C#] = "interpolación": permite meter variables adentro con { }

// void [C#] = función que no devuelve nada
// Log [NUESTRO] = función local que escribe una línea en el archivo de log
void Log(string msg)
{
    try   // try [C#] = "intentá esto; si falla, saltá al catch"
    {
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        // └─ File [.NET] clase  .  AppendAllText() [.NET] método  .  Environment.NewLine [.NET] (el salto de línea del SO)
        //    agrega una línea al final del archivo (no lo sobreescribe) → formato: [14:30:22.123] el mensaje
    }
    catch { }   // catch [C#] = "si algo falló arriba, hacé esto". Acá: nada (ignora el error)
    // └─ si falla escribir el log (disco lleno, permisos) → ignora el error silenciosamente
    //    El log es secundario, no puede romper el launcher
}

Log($"launcher starting; target URL: {apiUrl}");
// └─ primera línea del log: "empecé, voy a esta URL"

// ─── PASO 1: ¿LA API YA ESTÁ CORRIENDO? ──────────────────────────────────────
// await [C#] = "esperá que esto termine antes de seguir" (sin bloquear el proceso)
// if [C#]    = condición
// ! [C#]     = negación (NOT). Si NO está lista → entramos al if

if (!await ApiReadyAsync(TimeSpan.FromMilliseconds(800)))
// └─ ApiReadyAsync [NUESTRO] función (definida abajo)  .  TimeSpan [.NET] clase  .  FromMilliseconds() [.NET] método
//    Si la Api NO responde en 800ms → entra al bloque
{
    Log("Api not running, starting...");

    if (OperatingSystem.IsWindows() && ServiceController.IsServiceInstalled(ServiceName))
    // └─ OperatingSystem [.NET] clase  .  IsWindows() [.NET] método
    //    && [C#] = "Y" lógico  .  ServiceController [NUESTRO] clase  .  IsServiceInstalled() [NUESTRO] método
    //    ¿estamos en Windows Y el servicio está instalado?
    {
        Log("Service installed, SCM TryStart");
        ServiceController.TryStart(ServiceName);
        // └─ ServiceController [NUESTRO]  .  TryStart() [NUESTRO] — le pide a Windows que arranque el servicio
    }
    else   // else [C#] = "si no se cumplió el if"
    // └─ no es servicio → modo normal: lanzamos el .exe al lado
    {
        Log("Spawning sibling Api.exe");
        ApiLauncher.SpawnSibling();
        // └─ ApiLauncher [NUESTRO] clase  .  SpawnSibling() [NUESTRO] método
        //    busca PacCollector.Api.exe en la misma carpeta y lo lanza como proceso independiente
    }

    var ready = await WaitForApiAsync(TimeSpan.FromSeconds(15));
    // └─ ready [NUESTRO] variable  .  WaitForApiAsync [NUESTRO] función (abajo)  .  TimeSpan.FromSeconds() [.NET]
    //    espera hasta 15 segundos a que la Api responda al healthcheck (reintenta cada 250ms)

    Log($"Api ready after wait: {ready}");
    // └─ loguea si respondió o no

    if (!ready)
    {
        Log("ERROR: Api no respondio en 15s, abrimos browser igual");
        // └─ si no respondió en 15s → abrimos el browser igual
        //    el usuario verá un error de conexión en el browser, pero al menos arrancó algo
    }
}
else
{
    Log("Api was already running");
    // └─ la Api ya estaba corriendo (restart del Shell sin haber cerrado la Api)
}

// ─── PASO 2: ABRIR EL BROWSER ─────────────────────────────────────────────────
// Process.Start con UseShellExecute=true le dice a Windows:
// "abrí esta URL con el browser default" (Edge, Chrome, Firefox, lo que tenga el usuario)

try
{
    Log($"opening browser at {apiUrl}");
    Process.Start(new ProcessStartInfo
    // └─ Process [.NET] clase  .  Start() [.NET] método  .  new [C#] (crear objeto)  .  ProcessStartInfo [.NET] clase
    {
        FileName = apiUrl,
        // └─ FileName [.NET] propiedad. En vez de un .exe, le pasamos una URL → Windows la abre en el browser default.
        UseShellExecute = true,
        // └─ UseShellExecute [.NET] propiedad. true [C#]. "usá el shell de Windows" — necesario para abrir URLs
    });
    Log("browser launched, exiting");
}
catch (Exception e)
// └─ Exception [.NET] clase = cualquier error que ocurra adentro del try
//    e [NUESTRO] = la variable que contiene el error (nombre nuestro, podría ser cualquier cosa)
{
    Log($"failed to launch browser: {e.Message}");
    // └─ e.Message [.NET] propiedad — el texto del error. Loguea y sigue.
}

// ─── PASO 3: EL SHELL TERMINA ─────────────────────────────────────────────────
// Shell.exe sale. La Api sigue corriendo en background como proceso independiente.
// Cerrar el browser NO mata la Api.
// Para apagar la Api: Administrador de tareas → PacCollector.Api.exe → finalizar.

return 0;
// └─ return [C#]. Código de salida 0 = "terminé sin errores" (convención de todos los programas)

// ─── FUNCIONES LOCALES ────────────────────────────────────────────────────────
// Estas funciones están definidas ABAJO pero pueden llamarse ARRIBA.
// C# lo permite en top-level statements.

// Hace UN intento de GET a /api/health con el timeout dado.
// Devuelve true si respondió con 200 OK, false si no respondió o hubo error.
async Task<bool> ApiReadyAsync(TimeSpan timeout)
// └─ async [C#] = función asíncrona (puede usar await adentro)
//    Task<bool> [.NET] = va a devolver un bool, pero de forma asíncrona
//    bool [C#] = tipo verdadero/falso  .  TimeSpan [.NET] = tipo que representa una duración
//    ApiReadyAsync [NUESTRO] = nombre de la función  .  timeout [NUESTRO] = nombre del parámetro
{
    using var http = new HttpClient { Timeout = timeout };
    // └─ using [C#] acá = cuando termine el bloque, destruye el HttpClient automáticamente
    //    HttpClient [.NET] clase = cliente HTTP (como fetch en JavaScript)
    //    { Timeout = timeout } = le setea la propiedad Timeout [.NET] al crear el objeto
    try
    {
        var res = await http.GetAsync(apiUrl + "/api/health");
        // └─ res [NUESTRO] variable  .  http.GetAsync() [.NET] método — hace GET a la URL
        //    await [C#] = espera la respuesta sin bloquear
        return res.IsSuccessStatusCode;
        // └─ IsSuccessStatusCode [.NET] propiedad — true si el código HTTP es 200-299
    }
    catch { return false; }
    // └─ si hay cualquier error (timeout, conexión rechazada) → devuelve false
}

// Reintenta ApiReadyAsync cada 250ms hasta que responda o se acabe el tiempo.
// Devuelve true si la Api respondió antes del timeout, false si se agotó el tiempo.
async Task<bool> WaitForApiAsync(TimeSpan timeout)
// └─ WaitForApiAsync [NUESTRO] función  .  async [C#]  .  Task<bool> [.NET]  .  TimeSpan [.NET]
{
    var deadline = DateTime.UtcNow + timeout;
    // └─ deadline [NUESTRO] variable  .  DateTime [.NET] clase  .  UtcNow [.NET] propiedad (hora actual en UTC)
    //    momento límite = ahora + el tiempo dado

    while (DateTime.UtcNow < deadline)
    // └─ while [C#] = "mientras se cumpla esto, repetí el bloque"
    //    mientras no hayamos llegado al límite de tiempo → seguir intentando
    {
        if (await ApiReadyAsync(TimeSpan.FromMilliseconds(500))) return true;
        // └─ intenta con 500ms de timeout. Si respondió → return true [C#] inmediatamente.

        await Task.Delay(250);
        // └─ Task [.NET] clase  .  Delay() [.NET] método — espera 250ms sin bloquear el proceso
    }
    return false;
    // └─ se acabó el tiempo y nunca respondió → devuelve false
}
