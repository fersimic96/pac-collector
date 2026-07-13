namespace PacCollector.Infrastructure.Plugins.Print;

// que tipo de parser usar para un reporte print:
//   - LabelValue: reporte simple con lineas "Label: value" (FZP, CPP)
//   - Distillation: dos columnas + tabla IBP/FBP/curva (OptiPMD)
//   - OptiDist: layout CR-overwrite estilo Windows printer (OptiDist2)
//     requiere ademas spec.RequiresCrRender = true
public enum PrintReportKind
{
    LabelValue,
    Distillation,
    OptiDist,
}
