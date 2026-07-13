using System.Text.RegularExpressions;

namespace PacCollector.Infrastructure.Plugins.Print;

// regex compartidos para reportes print. Compilados via source generator (cero reflection).
internal static partial class PrintRegex
{
    // header: marker S/N: <serial> V <firmware>
    public static Regex BuildHeaderRegex(string marker)
        => new(
            $@"{Regex.Escape(marker)}\s+S/N:\s*(\S+)\s+V\s+(\S+)",
            RegexOptions.Compiled);

    [GeneratedRegex(@"(?m)^Date\s*:\s*(\d{1,2}\s+\w{3}\s+\d{4})\s+(\d{1,2}:\d{2})")]
    public static partial Regex ResultDate();

    [GeneratedRegex(@"(?m)^Operator\s*:\s*(\S+(?:\s+\S+)*?)\s*$")]
    public static partial Regex Operator();

    [GeneratedRegex(@"(?m)^Sample ID\s*:\s*(\S+(?:\s+\S+)*?)\s*$")]
    public static partial Regex SampleId();

    [GeneratedRegex(@"(?m)^Product\s*:\s*(.+?)\s*$")]
    public static partial Regex Product();

    [GeneratedRegex(@"Cycle/Result\s*:\s*(\d+)\s*/\s*(\d+)")]
    public static partial Regex CycleResult();

    [GeneratedRegex(@"(?m)^\s*(\d{1,2}\s+\w{3}\s+\d{4})\s+(\d{1,2}:\d{2})\s+AtmPrs")]
    public static partial Regex PmdRunDate();

    [GeneratedRegex(@"\s{2,}")]
    public static partial Regex MultipleSpaces();

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)\s*C\s*$")]
    public static partial Regex NumericCelsius();

    // fila de tabla de destilacion: IBP/FBP/N% + temperatura
    [GeneratedRegex(@"^\s*(IBP|FBP|\d+\s*%)\s+(-?\d+(?:\.\d+)?)")]
    public static partial Regex DistillationRow();

    // fila de recovery: %R N % %r N %
    [GeneratedRegex(@"%R\s+(\d+(?:\.\d+)?)\s*%\s+%r\s+(\d+(?:\.\d+)?)\s*%")]
    public static partial Regex RecoveryResidue();

    // ── OptiDist2 (Windows printer CR-overwrite, post-render) ──────────
    // formato fecha del OptiDist2: DD/MM/YYYY HH:MM:SS
    [GeneratedRegex(@"(\d{2}/\d{2}/\d{4})\s+(\d{2}:\d{2}:\d{2})")]
    public static partial Regex OptidistDate();

    // operator en columna derecha tras "Operat" (label truncado por CR-overwrite)
    [GeneratedRegex(@"Operat(.+?)(?:\s{3,}|$)")]
    public static partial Regex OptidistOperator();

    // product en linea con label truncado "Produ"
    [GeneratedRegex(@"Produ(.+?)(?:\s{3,}|$)")]
    public static partial Regex OptidistProduct();

    // sample id en linea con label truncado "Sampl"
    [GeneratedRegex(@"Sampl(.+?)(?:\s{3,}|$)")]
    public static partial Regex OptidistSampleId();

    // recovery percent en la seccion Distillation results
    [GeneratedRegex(@"Recovery\s*(\d+\.?\d*)\s*%")]
    public static partial Regex OptidistRecovery();

    // residue mL en la seccion Distillation results
    [GeneratedRegex(@"Residue\s+(\d+\.?\d*)\s*[mM][lL]")]
    public static partial Regex OptidistResidue();

    // IBP temp en la seccion Distillation table (pre-CR-render, raw)
    [GeneratedRegex(@"IBP\s+(\d+\.?\d*)")]
    public static partial Regex OptidistIbp();
}
