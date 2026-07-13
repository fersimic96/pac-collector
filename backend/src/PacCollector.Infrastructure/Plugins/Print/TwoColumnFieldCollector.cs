namespace PacCollector.Infrastructure.Plugins.Print;

// los reportes PMD vienen en formato de dos columnas: cada linea puede tener varios "label:value"
// separados por 2+ espacios. Esto parsea cada segmento y devuelve un dict label→value.
// universal: no depende del modelo de equipo, solo del formato print de dos columnas.
internal static class TwoColumnFieldCollector
{
    public static IReadOnlyDictionary<string, string> Collect(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var line in text.Split('\n'))
        {
            foreach (var seg in PrintRegex.MultipleSpaces().Split(line))
            {
                var segment = seg.Trim();
                if (segment.Length == 0) continue;
                var colon = segment.IndexOf(':');
                if (colon <= 0) continue;
                var label = segment[..colon].Trim();
                var value = CleanValue(segment[(colon + 1)..]);
                if (label.Length == 0 || value.Length == 0) continue;
                if (!result.ContainsKey(label))
                    result[label] = value;
            }
        }
        return result;
    }

    // saca el sufijo " C" (grados Celsius) de valores tipo "232.5 C" → "232.5"
    public static string CleanValue(string raw)
    {
        // sacar replacement chars (U+FFFD) que pueden venir de payloads no-UTF-8
        var noReplacement = new string(raw.Where(c => c != '�').ToArray());
        var trimmed = noReplacement.Trim();
        var m = PrintRegex.NumericCelsius().Match(trimmed);
        return m.Success ? m.Groups[1].Value : trimmed;
    }
}
