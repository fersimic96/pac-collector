namespace PacCollector.Infrastructure.Plugins.Print;

// spec declarativo de un equipo en modo print. Se carga desde JSON externo.
// para agregar un equipo nuevo basta con un .json en plugins/print/
public sealed class PrintPluginSpec
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AnalyzerType { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Version { get; set; } = "";

    public PrintReportKind Kind { get; set; } = PrintReportKind.LabelValue;

    // marcador que aparece en el payload para identificar el equipo (ej. "OptiFZP")
    public string HeaderMarker { get; set; } = "";

    // header regex custom; si es null se construye automatico:
    //   {HeaderMarker}\s+S/N:\s*(\S+)\s+V\s+(\S+)
    public string? HeaderRegexOverride { get; set; }

    // label distintivo del equipo que se extrae como extra (ej. "Freeze point", "Cloud point", "IBP")
    public string HeadlineLabel { get; set; } = "";

    // mapping label-del-print → key-del-Extra (ej. "Stop Temperature" → "StopTemperature")
    public List<PrintLabelMapping> ExtraFieldKeys { get; set; } = new();

    // metadata para la UI (descripciones de campos)
    public List<PrintFieldSpec> FieldSpecs { get; set; } = new();

    // si true, aplica CR-overwrite (Windows printer dot-matrix two-column
    // layout) al texto antes del field extraction. Necesario para OptiDist2
    // y cualquier equipo cuya driver imprime via \r-separated segments.
    // Independiente del Kind para que un futuro shape pueda reusar el renderer.
    public bool RequiresCrRender { get; set; }
}

// mapping de un campo a extraer del reporte print.
//
// Dos modos de extraccion, mutuamente excluyentes:
//   1) Label-based (default): cuando Pattern es null/vacio. Busca la linea
//      "Label: value" usando capture_label() — comportamiento legacy.
//   2) Regex-based: cuando Pattern tiene valor. Evalua el regex sobre el texto
//      completo y extrae el grupo de captura indicado por Group (default 1).
//
// Regex es la "via de escape" para campos que no encajan en label-value
// (ej. "Run #(\\d+)", "Calibration:\\s*([\\w-]+)\\s*\\([^)]+\\)"). Sin esto,
// el integrador queda forzado a editar C# para casos atipicos.
public sealed class PrintLabelMapping
{
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";

    // si se setea, usa regex en vez de label-based. El pattern se evalua
    // sobre el texto print con flags multilinea por default desde el caller.
    public string? Pattern { get; set; }

    // indice del grupo de captura del regex. Default 1 (primer grupo).
    public int Group { get; set; } = 1;
}

public sealed class PrintFieldSpec
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Group { get; set; } = "";
}
