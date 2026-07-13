// ══════════════════════════════════════════════════════════════════════════════
// ESTE ARCHIVO ES EL "COMPOSITION ROOT" — el corazón de la Api.
// Se lee de arriba a abajo como una receta:
//   1. importar código          (using)
//   2. inicializar el LOGGER    (Serilog + handlers globales de excepciones)
//   3. crear el "builder"        (preparar los ingredientes)
//   4. resolver dónde van los archivos en disco
//   5. crear los objetos únicos  (singletons de Infrastructure)
//   6. registrarlos en el DI container  (anotar en la lista de la "moza")
//   7. construir la app          (builder.Build)
//   8. configurar y arrancar     (app.Run — se queda vivo para siempre)
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA — de dónde viene cada palabra:
//   [C#]      = palabra reservada del lenguaje C#
//   [.NET]    = clase/método de Microsoft (viene con el framework)
//   [SERILOG] = clase/método de Serilog (paquete NuGet de logging)
//   [NUESTRO] = código que escribimos nosotros (en este proyecto)
// ══════════════════════════════════════════════════════════════════════════════

// ─── IMPORTACIONES ───────────────────────────────────────────────────────────
// using [C#] = "quiero usar código de este namespace"
// Los que empiezan con "System." son [.NET]. Los que empiezan con "PacCollector." son [NUESTRO].
using Microsoft.AspNetCore.Diagnostics;          // [.NET]    IExceptionHandlerFeature (para el middleware de errores)
using Serilog;                                    // [SERILOG] Log.Xxx() y LoggerConfiguration
using Serilog.Events;                             // [SERILOG] LogEventLevel
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

// ══════════════════════════════════════════════════════════════════════════════
// PASO 2 — INICIALIZAR EL LOGGER (Serilog) Y LOS HANDLERS GLOBALES
// ══════════════════════════════════════════════════════════════════════════════
// Antes de crear la app hay que dejar armado el sistema de logs para que TODO lo
// que pase — incluso una excepción en el arranque, o en un thread suelto — quede
// registrado en un archivo. Sin esto, si la app se muere en producción no hay
// forma de saber por qué.
//
// Los logs se escriben a:
//   Windows:      C:\ProgramData\PacCollector\logs\pac-collector-YYYY-MM-DD.log
//   macOS/Linux:  ~/.local/share/PacCollector/logs/pac-collector-YYYY-MM-DD.log
//
// Rotación DIARIA, retención 30 días (el resto se borra automáticamente).

var logsDir = Path.Combine(ResolveInitialDataDir(), "logs");   // [NUESTRO] función local (definida al final)
Directory.CreateDirectory(logsDir);                             // [.NET]    crea la carpeta si no existe

Log.Logger = new LoggerConfiguration()                          // [SERILOG] arranca la config del logger global
    .MinimumLevel.Information()                                 // [SERILOG] captura todo lo Information y arriba
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)  // [SERILOG] los mensajes de Microsoft.* solo si son Warning+
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)  // el "Now listening on..." sí lo queremos
    .Enrich.FromLogContext()                                    // [SERILOG] permite agregar contexto por scope
    .WriteTo.Console()                                          // [SERILOG] sale también por consola (cuando no es servicio)
    .WriteTo.File(                                              // [SERILOG] escribe en archivo con rotación
        Path.Combine(logsDir, "pac-collector-.log"),
        rollingInterval: RollingInterval.Day,                   // rotar por día
        retainedFileCountLimit: 30,                             // conservar los últimos 30 archivos
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();                                            // [SERILOG] finaliza la config

// ─── HANDLERS GLOBALES DE EXCEPCIONES ────────────────────────────────────────
// Estos dos handlers capturan excepciones que se escapan de cualquier lado del
// código y no fueron atrapadas por un try/catch. Sin esto, la app se muere
// silenciosamente y no queda rastro.

AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    // [.NET] evento que se dispara cuando una excepción llega hasta arriba de todo
    // sin ser atrapada. Cuando IsTerminating=true, el proceso se va a caer.
    var ex = e.ExceptionObject as Exception;
    Log.Fatal(ex, "Excepcion NO controlada — terminando={IsTerminating}", e.IsTerminating);
    Log.CloseAndFlush();   // fuerza el flush del buffer del logger al archivo antes de morir
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    // [.NET] excepción en un Task async que nadie estaba await-eando (fire-and-forget).
    // Antes de .NET 5 esto tumbaba el proceso; ahora solo se ignora. Nosotros la logueamos.
    Log.Error(e.Exception, "Excepcion en Task no observado (fire-and-forget)");
    e.SetObserved();       // le decimos al runtime que ya la manejamos
};

// ══════════════════════════════════════════════════════════════════════════════
// TODO EL RESTO DEL ARRANQUE VA ADENTRO DE UN try/catch/finally.
// Si algo falla en el startup, se loguea como Fatal y se cierra el logger limpio.
// ══════════════════════════════════════════════════════════════════════════════
try
{
    Log.Information("PAC Collector Api arrancando (pid={Pid}, dataDir={DataDir})",
        Environment.ProcessId, Path.GetDirectoryName(logsDir));

    // ─── PASO 3: CREAR EL BUILDER ────────────────────────────────────────────
    var builder = WebApplication.CreateBuilder(args);
    // └─ builder [NUESTRO] variable  .  WebApplication [.NET] clase  .  CreateBuilder() [.NET] método  .  args [.NET] (argumentos de la terminal)
    //    "builder" es el que arma la app antes de arrancarla. Existe solo durante la preparación.

    // ─── ENGANCHAR SERILOG COMO LOGGER PRINCIPAL ──────────────────────────────
    // Reemplaza el logger default de ASP.NET Core por Serilog para que TODOS
    // los ILogger<T> que se inyecten (en endpoints, servicios, etc.) escriban
    // a los mismos sinks (consola + archivo).
    builder.Host.UseSerilog();   // [SERILOG]

    // ─── DUAL-MODE: servicio de Windows o consola ────────────────────────────
    // dual-mode: con --service corre como Windows Service. Sin args corre standalone Kestrel.
    if (args.Contains("--service"))                  // if [C#]  .  args.Contains() [.NET] método — ¿vino "--service"?
        builder.Host.UseWindowsService();            // builder.Host [.NET]  .  UseWindowsService() [.NET] — modo servicio
    builder.WebHost.UseUrls("http://127.0.0.1:5174");
    // └─ builder.WebHost [.NET]  .  UseUrls() [.NET] — le dice a Kestrel (el servidor web de .NET) en qué URL escuchar
    //    127.0.0.1 = SOLO esta PC (loopback). Nadie desde la red puede entrar. Es a propósito (seguridad).

    // ─── PASO 4: RESOLVER LOS PATHS EN DISCO ─────────────────────────────────
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

    // ─── PASO 5: CREAR LOS OBJETOS ÚNICOS (singletons de Infrastructure) ──────
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

    // ─── PASO 6: REGISTRAR TODO EN EL DI CONTAINER ───────────────────────────
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

    // ─── registrar los servicios de Application (use cases + el pipeline) ─────
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
    // Nota: el "log" del ListenerManager antes escribia a Console. Ahora va a Serilog
    // (Log.Information) — así los eventos de red quedan en el archivo de logs.
    builder.Services.AddSingleton(sp => new ListenerManager(
        sp.GetRequiredService<ConfigStore>(),            // la config
        sp.GetRequiredService<HandleBeaconUseCase>(),    // qué hacer con el beacon
        sp.GetRequiredService<ReceiveSampleUseCase>(),   // qué hacer con una muestra LIMS
        sp.GetRequiredService<ReceivePrintUseCase>(),    // qué hacer con una impresión
        log: msg => Log.Information("[net] {Message}", msg)));   // [SERILOG] a dónde loguear la red

    // ── api helpers ──
    builder.Services.AddSingleton<NetworkInfoService>();  // [NUESTRO] info de red (IPs disponibles)
    builder.Services.AddSingleton<SystemService>();       // [NUESTRO] info del sistema (versión, uptime)
    builder.Services.AddSingleton(sp => new PluginUploadService(
        sp.GetRequiredService<PluginRegistryImpl>(),      // [NUESTRO] sube/recarga plugins desde el frontend
        limsPluginsOverrideDir,
        printPluginsOverrideDir));

    // ─── configuración de cómo se serializa el JSON hacia el frontend ────────
    // camelCase para propiedades + snake_case para enums (matchea convención del frontend React)
    // NO ignorar nulls: el frontend espera que todos los campos opcionales estén presentes con null
    builder.Services.ConfigureHttpJsonOptions(opt =>       // ConfigureHttpJsonOptions [.NET]
    {
        opt.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;        // [.NET] "IbpValue" → "ibpValue"
        opt.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;       // [.NET] nunca omitir campos null
        opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)); // [.NET] enums como texto snake_case
    });

    // ─── CORS: quién puede llamar a la Api desde un browser ──────────────────
    // permisivo (cualquier origen) — es aceptable porque la Api SOLO escucha en 127.0.0.1 (no expuesta a la red)
    builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>          // AddCors [.NET]
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));        // [.NET] permite todo (loopback, sin riesgo)

    // ─── PASO 7: CONSTRUIR LA APP ────────────────────────────────────────────
    var app = builder.Build();
    // └─ app [NUESTRO] var  .  builder.Build() [.NET] — termina la preparación y crea la app real.
    //    A partir de acá "builder" ya no se usa: nació "app" y es lo que corre.

    // ─── MIDDLEWARE DE EXCEPCIONES HTTP ──────────────────────────────────────
    // Cuando un endpoint tira una excepción sin atrapar, este middleware la
    // captura, la loguea con Serilog, y devuelve al cliente un JSON 500 uniforme
    // en vez de exponer stack traces al frontend.
    // IMPORTANTE: tiene que ir ANTES de UseCors/UseWebSockets/rutas para que
    // capture todo lo que pase adelante.
    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<IExceptionHandlerFeature>();   // [.NET] la excepción real
        var ex = feature?.Error;
        Log.Error(ex, "Excepcion HTTP no controlada en {Method} {Path}",
            ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "internal_server_error",
            message = "El servidor tuvo un error interno. Ver logs en pac-collector-YYYY-MM-DD.log"
        });
    }));

    app.UseSerilogRequestLogging();  // [SERILOG] loguea cada request HTTP (método, ruta, status, tiempo)
    app.UseCors();          // app.UseCors() [.NET] — activa la política CORS de arriba
    app.UseWebSockets();    // app.UseWebSockets() [.NET] — habilita conexiones WebSocket (eventos en vivo)

    // ─── SERVIR EL FRONTEND REACT (los archivos de wwwroot/) ─────────────────
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
        Log.Information("Sirviendo frontend desde {Wwwroot}", wwwroot);
    }
    else
    {
        Log.Warning("Carpeta wwwroot NO encontrada en {Wwwroot} — la UI estara en blanco", wwwroot);
    }

    // ─── ARRANCAR LOS SERVIDORES DE RED (si la config lo pide) ───────────────
    // Acá es donde la Api empieza a ESCUCHAR a los equipos PAC (UDP :3000, TCP :9980).
    if (configStore.Snapshot().General.AutoStartServer)     // Snapshot() [NUESTRO] lee la config actual
    {
        app.Services.GetRequiredService<ListenerManager>().StartLims();   // GetRequiredService [.NET] trae el ListenerManager; StartLims() [NUESTRO] arranca UDP+TCP
        if (configStore.Snapshot().General.PrintServerEnabled)
            app.Services.GetRequiredService<ListenerManager>().StartPrint();  // StartPrint() [NUESTRO] arranca el servidor de impresión :631
    }

    // ─── PASO 8 (parte a): REGISTRAR LAS RUTAS HTTP ──────────────────────────
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

    Log.Information("Api lista — escuchando en http://127.0.0.1:5174");

    // ─── PASO 8 (parte b): ARRANCAR Y QUEDARSE VIVO ──────────────────────────
    app.Run();
    // └─ app.Run() [.NET] — arranca Kestrel y BLOQUEA acá para siempre, escuchando requests.
    //    El programa no pasa de esta línea hasta que alguien lo cierra (Ctrl+C o matar el proceso).
}
catch (Exception ex)
{
    // Cualquier excepción NO manejada durante el arranque termina acá.
    // La logueamos como Fatal y re-lanzamos para que el proceso muera con exit code != 0.
    Log.Fatal(ex, "Api fallo al arrancar");
    throw;
}
finally
{
    // SIEMPRE al salir (crash o cierre limpio), forzar el flush del logger.
    // Sin esto, los últimos mensajes en el buffer se pierden.
    Log.CloseAndFlush();
}

// ─── FUNCIÓN LOCAL: ¿dónde guardar los datos? ────────────────────────────────
// static [C#] = función que no depende de ningún objeto.  IConfiguration [.NET] = acceso a la config.
static string ResolveDataDir(IConfiguration config)
{
    var fromConfig = config["DataDir"];                          // ¿hay un override "DataDir" en la config?
    if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig; // string.IsNullOrWhiteSpace [.NET] — si lo hay, usarlo
    return DefaultDataDir();
}

// ─── FUNCIÓN LOCAL: dataDir inicial (para el logger, antes que exista el builder) ──
// Solo mira env var + OS. No lee appsettings.json porque todavía no hay builder.
// En 99% de los casos coincide con lo que resuelve ResolveDataDir después.
static string ResolveInitialDataDir()
{
    var envOverride = Environment.GetEnvironmentVariable("PAC_COLLECTOR_DATA_DIR");
    if (!string.IsNullOrWhiteSpace(envOverride)) return envOverride;
    return DefaultDataDir();
}

// ─── FUNCIÓN LOCAL: path default (según SO) ──────────────────────────────────
static string DefaultDataDir()
{
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
