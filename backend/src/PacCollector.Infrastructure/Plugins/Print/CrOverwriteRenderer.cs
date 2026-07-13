using System.Text;

namespace PacCollector.Infrastructure.Plugins.Print;

// simula el buffer CR-overwrite que usa el driver de impresora de Windows
// para layouts de dos columnas (estilo dot-matrix / line printer). El driver
// emite varios segmentos por linea logica, separados por \r, cada uno
// escrito desde columna 0; el ultimo char no-space en cada posicion gana.
//
// Ej:
//   "Hello\rXYZ"     -> "XYZlo"  (X,Y,Z sobrescriben H,e,l; l,o quedan)
//   "Hello\r  X"     -> "HeXlo"  (spaces no sobrescriben; X sobrescribe l)
//
// Sin esto, los reportes OptiDist2 muestran labels fragmentados ("Operat",
// "Produ", "Sampl") porque cada segmento \r comienza desde columna 0.
//
// Port directo de pac-collector/src-tauri/.../pac_family_print_plugin.rs:568-588
internal static class CrOverwriteRenderer
{
    public static string Render(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = new StringBuilder(text.Length);
        var lines = text.Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            var logicalLine = lines[li];
            var buf = new List<char>();
            foreach (var segment in logicalLine.Split('\r'))
            {
                for (var i = 0; i < segment.Length; i++)
                {
                    var ch = segment[i];
                    if (buf.Count <= i)
                    {
                        // extender buffer con spaces hasta la posicion i
                        while (buf.Count <= i) buf.Add(' ');
                    }
                    // non-space sticky: solo escribe space si el slot ya esta vacio
                    if (ch != ' ' || buf[i] == ' ')
                    {
                        buf[i] = ch;
                    }
                }
            }
            // trim trailing spaces de la linea rendered
            while (buf.Count > 0 && buf[^1] == ' ') buf.RemoveAt(buf.Count - 1);
            for (var i = 0; i < buf.Count; i++) result.Append(buf[i]);
            if (li < lines.Length - 1) result.Append('\n');
        }
        return result.ToString();
    }
}
