// ══════════════════════════════════════════════════════════════════════════════
// ESTE ARCHIVO ES EL "COMPOSITION ROOT" — el corazón de la Api.
// Se lee de arriba a abajo como una receta:
//   1. importar código          (using)
//   2. crear el "builder"        (preparar los ingredientes)
//   3. resolver dónde van los archivos en disco
//   4. crear los objetos únicos  (singletons de Infrastructure)
//   5. registrarlos en el DI container  (anotar en la lista de la "moza")
//   6. construir la app          (builder.Build)
//   7. configurar y arrancar     (app.Run — se queda vivo para siempre)
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA — de dónde viene cada palabra:
//   [C#]      = palabra reservada del lenguaje C#
//   [.NET]    = clase/método de Microsoft (viene con el framework)
//   [NUESTRO] = código que escribimos nosotros (en este proyecto)
// ══════════════════════════════════════════════════════════════════════════════

// ─── IMPORTACIONES ───────────────────────────────────────────────────────────
// using [C#] = "quiero usar código de este namespace"
// Los que empiezan con "System." son [.NET]. Los que empiezan con "PacCollector." son [NUESTRO].
using System.Text.Json;                          // [.NET]    serialización JSON
using System.Text.Json.Serialization;            // [.NET]    opciones de serialización (enums, nulls)
using PacCollector.Api.Endpoints;                // [NUESTRO] los MapXxxEndpoints (las rutas HTTP)
using PacCollector.Api.Services;                 // [NUESTRO] helpers de la Api (Network, System, PluginUpload)
using PacCollector.Application.Services;         // [NUESTRO] SampleProcessingService (el pipeline)
using PacCollector.Application.UseCases;         // [NUESTRO] los casos de uso
using PacCollector.Domain.Ports;                 // [NUESTRO] las interfaces (contratos)
using PacCollector.Infrastructure.Config;        // [NUESTRO] ConfigStore, AppConfig
using PacCollector.Infrastructure.EventBus;      // [NUESTRO] ChannelEventBus
using PacCollector.Infrastructure.Filesystem;    // [NUESTRO] FileWriterImpl, AtomicWriter
using PacCollector.Infrastructure.Hotfolder;     // [NUESTRO] plantillas de exportación a LIMS
using PacCollector.Infrastructure.Network;       // [NUESTRO] UdpServer, TcpServer, ListenerManager
using PacCollector.Infrastructure.Persistence;   // [NUESTRO] los repositorios (Json, InMemory)
using PacCollector.Infrastructure.Plugins;       // [NUESTRO] PluginRegistryImpl, parsers

// ─── PASO 2: CREAR EL BUILDER ────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);
// └─ builder [NUESTRO] variable  .  WebApplication [.NET] clase  .  CreateBuilder() [.NET] método  .  args [.NET] (argumentos de la terminal)
//    "builder" es el que arma la app antes de arrancarla. Existe solo durante la preparación.

// ─── DUAL-MODE: servicio de Windows o consola ────────────────────────────────
// dual-mode: con --service corre como Windows Service. Sin args corre standalone Kestrel.
if (args.Contains("--service"))                  // if [C#]  .  args.Contains() [.NET] método — ¿vino "--service"?
    builder.Host.UseWindowsService();            // builder.Host [.NET]  .  UseWindowsService() [.NET] — modo servicio
builder.WebHost.UseUrls("http://127.0.0.1:5174");
// └─ builder.WebHost [.NET]  .  UseUrls() [.NET] — le dice a Kestrel (el servidor web de .NET) en qué URL escuchar
//    127.0.0.1 = SOLO esta PC (loopback). Nadie desde la red puede entrar. Es a propósito (seguridad).

// ─── PASO 3: RESOLVER LOS PATHS EN DISCO ─────────────────────────────────────
// Primero calcula DÓNDE va cada archivo/carpeta (solo texto, no toca disco todavía).
// data paths (resueltos contra el directorio del usuario)
var dataDir = ResolveDataDir(builder.Configuration);
// └─ ResolveDataDir [NUESTRO] función (definida abajo)  .  builder.Configuration [.NET] (lee appsettings.json / env)
//    dataDir = carpeta raíz de datos. En Windows: C:\ProgramData\PacCollector
var dbDir = Path.Combine(dataDir, "db");                                        // Path.Combine [.NET] — solo arma texto
var recentDir = Path.Combine(dataDir, "recent");
var configPath = Path.Combine(dataDir, "settings.json");                        // el archivo de configuración
var instrumentsPath = Path.Combine(dataDir, "instruments.json");               // la tabla de equipos
var limsPluginsOverrideDir = Path.Combine(dataDir, "plugins", "lims");         // plugins LIMS personalizados
var printPluginsOverrideDir = Path.Combine(dataDir, "plugins", "print");       // plugins de impresión
var hotfolderTemplatesOverrideDir = Path.Combine(dataDir, "hotfolder-templates");

// Ahora sí crea las carpetas en el disco (si ya existen, no hace nada).
Directory.CreateDirectory(dataDir);                    // Directory [.NET]  .  CreateDirectory() [.NET]
Directory.CreateDirectory(dbDir);
Directory.CreateDirectory(recentDir);
Directory.CreateDirectory(limsPluginsOverrideDir);
Directory.CreateDirectory(printPluginsOverrideDir);
Directory.CreateDirectory(hotfolderTemplatesOverrideDir);

// ─── PASO 4: CREAR LOS OBJETOS ÚNICOS (singletons de Infrastructure) ──────────
// Estos 4 objetos se crean UNA sola vez acá porque necesitan los paths de arriba.
// "singleton" = existe UNA sola instancia en toda la app, compartida por todos.
var configStore = ConfigStore.Load(configPath);
// └─ configStore [NUESTRO] var  .  ConfigStore [NUESTRO] clase  .  Load() [NUESTRO] método — lee settings.json a memoria
var instrumentRepo = JsonInstrumentRepository.Load(instrumentsPath);
// └─ JsonInstrumentRepository [NUESTRO]  .  Load() [NUESTRO] — lee instruments.json a memoria
var pluginRegistry = PluginRegistryImpl.LoadBuiltin(limsPluginsOverrideDir, printPluginsOverrideDir);
// └─ PluginRegistryImpl [NUESTRO]  .  LoadBuiltin() [NUESTRO] — carga los parsers de equipos
var eventBus = new ChannelEventBus();
// └─ new [C#] (crear objeto)  .  ChannelEventBus [NUESTRO] — el bus de eventos (pub/sub para el WebSocket)

// ─── PASO 5: REGISTRAR TODO EN EL DI CONTAINER ───────────────────────────────
// builder.Services [.NET] = la "lista de la moza": qué objetos existen y cómo darlos.
// AddSingleton [.NET] = "creá UNO solo y dáselo a todo el que lo pida".
// Los < > [C#] = genéricos: le decís QUÉ TIPO estás registrando.
//
// PATRÓN CLAVE: se registra la INTERFAZ apuntando a la IMPLEMENTACIÓN.
//   AddSingleton<IInstrumentRepository>(instrumentRepo)
//               └── el contrato (Domain) ──┘   └── quién lo cumple (Infrastructure)
//   Así, cuando un UseCase pide "IInstrumentRepository", la moza le da "instrumentRepo".
//   El UseCase nunca sabe que abajo hay un JSON.

builder.Services.AddSingleton(configStore);                                 // registra ConfigStore (sin interfaz, se pide directo)
builder.Services.AddSingleton<IInstrumentRepository>(instrumentRepo);       // interfaz → implementación JSON
builder.Services.AddSingleton<IPluginRegistry>(pluginRegistry);            // interfaz → registry de plugins
builder.Services.AddSingleton(pluginRegistry);                             // también la clase concreta (para upload/reload)
builder.Services.AddSingleton<ChannelEventBus>(eventBus);                  // la clase concreta del bus
builder.Services.AddSingleton<IEventBus>(eventBus);                        // y también su interfaz (mismo objeto)
builder.Services.AddSingleton<ISampleRepository>(_ => new InMemorySampleRepository());
// └─ acá el patrón es distinto: en vez de un objeto ya creado, le pasamos una FÁBRICA.
//    _ => new InMemorySampleRepository()  =  "cuando alguien lo pida, creá uno así"
//    El "_" [C#] = "recibo un parámetro pero no lo uso". Las muestras viven en RAM (no en disco).

// hotfolder templates: embedded built-in + override en disco.
// Override es tolerante a JSON malo (skip + log), no rompe boot.
var hotfolderTemplates = HotfolderTemplateLoader.LoadAll(hotfolderTemplatesOverrideDir)
    .ToDictionary(t => t.Name, StringComparer.Ordinal);
// └─ HotfolderTemplateLoader [NUESTRO]  .  LoadAll() [NUESTRO]  .  ToDictionary() [.NET] (arma un diccionario nombre→plantilla)

builder.Services.AddSingleton<IFileWriter>(sp =>
    new FileWriterImpl(dbDir, recentDir, sp.GetRequiredService<ConfigStore>(), hotfolderTemplates));
// └─ otra fábrica: "sp" = el service provider (la moza misma).
//    sp.GetRequiredService<ConfigStore>() [.NET] = "traeme el ConfigStore que ya registré arriba".
//    Así FileWriterImpl recibe el ConfigStore sin que lo creemos a mano.

// ─── registrar los servicios de Application (use cases + el pipeline) ─────────
// Estos NO reciben objetos ya creados: solo el TIPO. La moza sabe construirlos sola,
// porque sus dependencias (repos, eventBus, etc.) ya están registradas arriba.
builder.Services.AddSingleton<SampleProcessingService>();     // el pipeline (corazón)
builder.Services.AddSingleton<HandleBeaconUseCase>();         // procesa el beacon UDP
builder.Services.AddSingleton<ReceiveSampleUseCase>();        // recibe muestra LIMS
builder.Services.AddSingleton<ReceivePrintUseCase>();         // recibe muestra por impresión
builder.Services.AddSingleton<ListInstrumentsUseCase>();      // lista equipos
builder.Services.AddSingleton<ListSamplesUseCase>();          // lista muestras
builder.Services.AddSingleton<UpdateInstrumentAliasUseCase>();// renombra un equipo

// ── network listener manager: el que arranca/para los servidores UDP y TCP ──
builder.Services.AddSingleton(sp => new ListenerManager(
    // fábrica: ListenerManager [NUESTRO] necesita varias cosas → se las pedimos a la moza (sp)
    sp.GetRequiredService<ConfigStore>(),            // la config
    sp.GetRequiredService<HandleBeaconUseCase>(),    // qué hacer con el beacon
    sp.GetRequiredService<ReceiveSampleUseCase>(),   // qué hacer con una muestra LIMS
    sp.GetRequiredService<ReceivePrintUseCase>(),    // qué hacer con una impresión
    log: msg => Console.WriteLine($"[net] {msg}")));  // Console.WriteLine [.NET] — a dónde loguear la red

// ── api helpers ──
builder.Services.AddSingleton<NetworkInfoService>();  // [NUESTRO] info de red (IPs disponibles)
builder.Services.AddSingleton<SystemService>();       // [NUESTRO] info del sistema (versión, uptime)
builder.Services.AddSingleton(sp => new PluginUploadService(
    sp.GetRequiredService<PluginRegistryImpl>(),      // [NUESTRO] sube/recarga plugins desde el frontend
    limsPluginsOverrideDir,
    printPluginsOverrideDir));

// ─── configuración de cómo se serializa el JSON hacia el frontend ────────────
// camelCase para propiedades + snake_case para enums (matchea convención del frontend React)
// NO ignorar nulls: el frontend espera que todos los campos opcionales estén presentes con null
builder.Services.ConfigureHttpJsonOptions(opt =>       // ConfigureHttpJsonOptions [.NET]
{
    opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;        // [.NET] "IbpValue" → "ibpValue"
    opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;       // [.NET] nunca omitir campos null
    opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)); // [.NET] enums como texto snake_case
});

// ─── CORS: quién puede llamar a la Api desde un browser ──────────────────────
// permisivo (cualquier origen) — es aceptable porque la Api SOLO escucha en 127.0.0.1 (no expuesta a la red)
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>          // AddCors [.NET]
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));        // [.NET] permite todo (loopback, sin riesgo)

// ─── PASO 6: CONSTRUIR LA APP ────────────────────────────────────────────────
var app = builder.Build();
// └─ app [NUESTRO] var  .  builder.Build() [.NET] — termina la preparación y crea la app real.
//    A partir de acá "builder" ya no se usa: nació "app" y es lo que corre.
app.UseCors();          // app.UseCors() [.NET] — activa la política CORS de arriba
app.UseWebSockets();    // app.UseWebSockets() [.NET] — habilita conexiones WebSocket (eventos en vivo)

// ─── SERVIR EL FRONTEND REACT (los archivos de wwwroot/) ─────────────────────
// OJO — bug histórico (v0.2.1): en single-file publish, AppContext.BaseDirectory
// apunta a una carpeta TEMPORAL, NO donde está el .exe. Por eso usamos
// Environment.ProcessPath para encontrar la carpeta real del ejecutable.
var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? typeof(Program).Assembly.Location)
    ?? AppContext.BaseDirectory;
// └─ Environment.ProcessPath [.NET] = path real del .exe en ejecución
//    ?? [C#] = "si eso es null, usá lo otro"  .  AppContext.BaseDirectory [.NET] = último fallback
var wwwroot = Path.Combine(exeDir, "wwwroot");
if (Directory.Exists(wwwroot))          // Directory.Exists [.NET] — ¿está la carpeta del frontend?
{
    var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwroot); // [.NET]
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });  // [.NET] sirve index.html en "/"
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });     // [.NET] sirve JS/CSS/imágenes
    Console.WriteLine($"[wwwroot] serving frontend from {wwwroot}");
}
else
{
    Console.WriteLine($"[wwwroot] NOT FOUND at {wwwroot} - UI will be blank");     // si falta → UI en blanco
}

// ─── ARRANCAR LOS SERVIDORES DE RED (si la config lo pide) ───────────────────
// Acá es donde la Api empieza a ESCUCHAR a los equipos PAC (UDP :3000, TCP :9980).
if (configStore.Snapshot().General.AutoStartServer)     // Snapshot() [NUESTRO] lee la config actual
{
    app.Services.GetRequiredService<ListenerManager>().StartLims();   // GetRequiredService [.NET] trae el ListenerManager; StartLims() [NUESTRO] arranca UDP+TCP
    if (configStore.Snapshot().General.PrintServerEnabled)
        app.Services.GetRequiredService<ListenerManager>().StartPrint();  // StartPrint() [NUESTRO] arranca el servidor de impresión :631
}

// ─── PASO 7 (parte a): REGISTRAR LAS RUTAS HTTP ──────────────────────────────
// Cada MapXxxEndpoints [NUESTRO] es un extension method que registra su grupo de rutas.
app.MapHealthEndpoints();       // GET /api/health
app.MapSampleEndpoints();       // /api/samples...
app.MapInstrumentEndpoints();   // /api/instruments...
app.MapPluginEndpoints();       // /api/plugins...
app.MapConfigEndpoints();       // /api/config...
app.MapListenerEndpoints();     // /api/listeners... (arrancar/parar red desde la UI)
app.MapNetworkEndpoints();      // /api/network...
app.MapSystemEndpoints();       // /api/system...
app.MapWebSocketEndpoints();    // /ws — eventos en vivo al frontend

// ─── PASO 7 (parte b): ARRANCAR Y QUEDARSE VIVO ──────────────────────────────
app.Run();
// └─ app.Run() [.NET] — arranca Kestrel y BLOQUEA acá para siempre, escuchando requests.
//    El programa no pasa de esta línea hasta que alguien lo cierra (Ctrl+C o matar el proceso).

// ─── FUNCIÓN LOCAL: ¿dónde guardar los datos? ────────────────────────────────
// static [C#] = función que no depende de ningún objeto.  IConfiguration [.NET] = acceso a la config.
static string ResolveDataDir(IConfiguration config)
{
    var fromConfig = config["DataDir"];                          // ¿hay un override "DataDir" en la config?
    if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig; // string.IsNullOrWhiteSpace [.NET] — si lo hay, usarlo
    var basePath = OperatingSystem.IsWindows()                   // OperatingSystem.IsWindows() [.NET]
        ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)   // Windows → C:\ProgramData
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);   // Mac/Linux → ~/.local/share
    // └─ el ?  :  es el "operador ternario" [C#] = un if/else en una línea (condición ? siEsCierto : siEsFalso)
    return Path.Combine(basePath, "PacCollector");
}

// ─── truco para los tests ────────────────────────────────────────────────────
// partial class [C#] — expone la clase Program (que es implícita en top-level statements)
// para que WebApplicationFactory<Program> pueda arrancar la Api en los tests de integración.
public partial class Program { }
