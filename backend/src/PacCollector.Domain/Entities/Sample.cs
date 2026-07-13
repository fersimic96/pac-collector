// ══════════════════════════════════════════════════════════════════════════════
// ENTIDAD Sample — representa UNA muestra analítica (un análisis de combustible).
// Es el dato central del sistema: todo el pipeline existe para producir uno de estos
// y guardarlo. Vive en Domain porque describe QUÉ es una muestra, sin saber cómo
// llega por la red ni cómo se guarda en disco.
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
using PacCollector.Domain.ValueObjects;   // [NUESTRO] trae AnalyzerSerial y DistillationCurve

namespace PacCollector.Domain.Entities;

// class [C#] = molde de objeto.  sealed [C#] = nadie puede heredar de Sample.
// A diferencia del "record" de los eventos, esta es una class MUTABLE: el pipeline
// la va llenando campo por campo mientras procesa el mensaje del equipo.
public sealed class Sample
{
    // Cada línea es una PROPIEDAD (un campo del objeto). Se leen así:
    //   { get; set; } [C#] = "se puede leer Y escribir" (propiedad auto-implementada)
    //   = string.Empty      = valor inicial por defecto (texto vacío, no null)

    public string Uuid { get; set; } = string.Empty;              // id único interno (lo generamos nosotros)
    public AnalyzerSerial Serial { get; set; }                    // AnalyzerSerial [NUESTRO] = nro de serie validado del equipo
    public string AnalyzerType { get; set; } = string.Empty;      // tipo de equipo: "OptiPMD", "OptiDist"...
    public string SampleIdentifier { get; set; } = string.Empty;  // id de la muestra según el equipo

    public string? Operator { get; set; }   // string? [C#] = puede ser null. Quién operó el equipo
    public string? Program { get; set; }    // método/programa de análisis usado

    // DateTimeOffset? [.NET]+[C#] = fecha+hora que PUEDE faltar (el "?" la hace opcional)
    public DateTimeOffset? StartAt { get; set; }   // cuándo empezó el análisis
    public DateTimeOffset? EndAt { get; set; }     // cuándo terminó

    // double? [C#] = número decimal opcional. Son los RESULTADOS del análisis:
    public double? Ibp { get; set; }        // Initial Boiling Point — punto inicial de ebullición (°C)
    public double? Fbp { get; set; }        // Final Boiling Point — punto final de ebullición (°C)
    public double? Residue { get; set; }    // residuo (%)
    public double? Recovery { get; set; }   // recuperación (%)
    public double? FbpVolume { get; set; }  // volumen al FBP

    public bool? EndOfTest { get; set; }       // bool? [C#] = verdadero/falso opcional. ¿El test terminó completo?
    public ulong? AlarmBitmask { get; set; }   // ulong? [C#] = entero grande opcional. Banderas de alarmas (bit a bit)

    // DistillationCurve [NUESTRO] = la curva de destilación (lista de puntos % vs °C).
    // = DistillationCurve.Empty() = arranca vacía en vez de null.
    public DistillationCurve Curve { get; set; } = DistillationCurve.Empty();

    // SortedDictionary [.NET] = diccionario ordenado (clave→valor). Guarda campos "extra"
    // que el equipo mandó pero que no tienen un campo propio arriba.
    // StringComparer.Ordinal [.NET] = compara claves como texto crudo (rápido, sin locale).
    public SortedDictionary<string, string> Extra { get; set; } = new(StringComparer.Ordinal);

    public string? SourceIp { get; set; }              // desde qué IP llegó
    public DateTimeOffset ReceivedAt { get; set; }     // cuándo LA RECIBIMOS nosotros (siempre presente)
    public string RawJson { get; set; } = string.Empty;// el JSON crudo original del equipo (para auditoría)

    // ─── MÉTODOS: pequeñas preguntas sobre la muestra ────────────────────────
    // El "=>" [C#] es un método de una sola línea (expression body).

    public bool IsComplete() => EndOfTest ?? false;
    // └─ ?? [C#] = "si EndOfTest es null, devolvé false". ¿El test terminó?

    public bool HasAlarms() => AlarmBitmask is { } b && b != 0;
    // └─ "is { } b" [C#] = "si AlarmBitmask NO es null, guardalo en la variable b"
    //    && b != 0 = "y además b no es cero" → hay alarmas activas

    public bool HasCurve() => !Curve.IsEmpty;
    // └─ ! [C#] = negación. "¿la curva NO está vacía?" → tiene puntos de destilación
}
