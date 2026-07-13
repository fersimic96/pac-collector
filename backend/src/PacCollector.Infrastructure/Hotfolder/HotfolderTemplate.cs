namespace PacCollector.Infrastructure.Hotfolder;

// spec declarativo de un formato de output para hotfolder.
// Permite definir templates de archivo sin compilar — el integrador agrega
// formatos nuevos drop-eando un .json en DataDir/hotfolder-templates/.
//
// El renderer evalua cada linea del template substituyendo tokens segun el
// Sample. Soporta:
//   - Tokens: {Path[:format][|fallback1|fallback2|...|literal]}
//     Path: Serial, AnalyzerType, SampleIdentifier, Operator, Program,
//           Ibp, Fbp, Residue, Recovery, FbpVolume, EndOfTest,
//           StartAt, EndAt, ReceivedAt, SourceIp, Alias,
//           Extra.<key>
//     format: .NET format string ("F1", "yyyy-MM-ddTHH:mm:ss.000", "D4", "R")
//     fallback: otro path o literal si no hay match
//
//   - Lineas condicionales: prefijo "?{Path}?" — solo emite si Path es no-null/no-empty
//     Ej: "?{Operator}?OperatorId;{Operator}" — emite la linea solo si hay operator
//
//   - {Curve.ForEach: <row-template>} — expande a N lineas, una por punto de la curva.
//     Dentro del row-template estan disponibles: {PctRecovered}, {PctLabel} (= "5%" o "12.5%"),
//     {PctPadded4} (= "0005"), {TemperatureC} (con format opcional).
//
//   - {Extra.ForEach: <row-template>} — expande a N lineas, una por entrada del dict Extra.
//     Dentro del row-template estan disponibles: {Key}, {Value}.
public sealed class HotfolderTemplate
{
    public string Name { get; set; } = "";
    public string FilenameTemplate { get; set; } = "{Serial}_{SampleIdentifier}_{StartAt:yyyyMMdd_HHmm}.txt";
    public string Encoding { get; set; } = "utf-8";

    // CRLF | LF (default LF)
    public string LineEnding { get; set; } = "LF";

    // si true, una linea sin contenido renderizado tampoco emite el line-ending
    // (util para evitar lineas vacias residuales tras condiciones falladas)
    public bool TrimEmptyLines { get; set; }

    // lineas del archivo, en orden. Cada una puede ser literal, tener tokens,
    // condicional con prefijo "?{X}?", o ForEach especial.
    public List<string> Lines { get; set; } = new();
}
