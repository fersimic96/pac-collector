using System.Text.RegularExpressions;

namespace PacCollector.Infrastructure.Plugins.Print;

// dispatcher de extraccion de un PrintLabelMapping sobre el texto del reporte.
// si el mapping tiene Pattern, evalua regex y extrae el grupo de captura indicado.
// si no, delega a la extraccion label-based "Label: value" (legacy).
//
// devuelve null cuando no hay match (no diferencia "missing" de "empty"; ambos
// son indistinguibles desde el punto de vista del consumidor).
internal static class LabelMappingExtractor
{
    public static string? Extract(PrintLabelMapping mapping, string text)
    {
        if (string.IsNullOrEmpty(mapping.Pattern))
            return ExtractByLabel(mapping.Label, text);

        return ExtractByRegex(mapping.Pattern!, mapping.Group, text);
    }

    private static string? ExtractByLabel(string label, string text)
    {
        if (string.IsNullOrEmpty(label)) return null;
        var pat = $@"(?m)^\s*{Regex.Escape(label)}\s*:\s*(.+?)\s*$";
        Regex re;
        try { re = new Regex(pat); }
        catch (ArgumentException) { return null; }
        var m = re.Match(text);
        if (!m.Success) return null;
        return TwoColumnFieldCollector.CleanValue(m.Groups[1].Value);
    }

    private static string? ExtractByRegex(string pattern, int groupIndex, string text)
    {
        Regex re;
        try
        {
            // Multiline ON por default — patterns suelen usar ^/$ por linea.
            re = new Regex(pattern, RegexOptions.Multiline);
        }
        catch (ArgumentException)
        {
            // pattern invalido en el spec: tratado igual que un no-match para
            // que no rompa el procesamiento de la muestra entera. El integrador
            // detecta el error via `spec test` (CLI).
            return null;
        }
        var m = re.Match(text);
        if (!m.Success) return null;
        if (groupIndex < 0 || groupIndex >= m.Groups.Count) return null;
        var v = m.Groups[groupIndex].Value;
        return TwoColumnFieldCollector.CleanValue(v);
    }
}
