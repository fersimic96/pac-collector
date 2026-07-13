using System.Text;

namespace PacCollector.Infrastructure.Plugins.Print;

// saca los escapes PCL (printer command language) y deja el texto plano del reporte.
// universal: no depende del modelo de equipo. Tambien trunca el bloque HP-GL final ("%1BIN;").
internal static class PclStripper
{
    private const char Esc = '\u001B';
    private const string HpglMarker = "%1BIN;";

    public static string Strip(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (c == Esc)
            {
                if (i + 1 >= input.Length) break;
                var param = input[i + 1];
                var paramCode = (uint)param;
                var isParameterized = (paramCode >= 0x21 && paramCode <= 0x2F)
                    || (paramCode >= 0x3C && paramCode <= 0x3F);
                if (isParameterized)
                {
                    i += 2;
                    while (i < input.Length)
                    {
                        var c2 = input[i];
                        var cp = (uint)c2;
                        i++;
                        if ((cp >= 0x40 && cp <= 0x5A) || c2 == '@' || c2 == '^')
                            break;
                    }
                }
                else
                {
                    i += 2;
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        var stripped = sb.ToString();
        var hpglIdx = stripped.IndexOf(HpglMarker, StringComparison.Ordinal);
        return hpglIdx >= 0 ? stripped[..hpglIdx] : stripped;
    }
}
