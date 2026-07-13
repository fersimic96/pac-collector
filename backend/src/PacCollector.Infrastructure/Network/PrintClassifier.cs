namespace PacCollector.Infrastructure.Network;

internal enum PrintClassification
{
    Http,
    Raw,
    Indeterminate,
}

// clasifica una conexion entrante al puerto 631:
//   - HTTP/IPP: empieza con POST/GET/HEAD/PUT/OPTIONS/DELETE/PATCH + espacio
//   - Raw: cualquier otra cosa que no parezca HTTP
//   - Indeterminate: prefijo ASCII corto que podria ser HTTP pero todavia no se sabe
internal static class PrintClassifier
{
    private static readonly byte[][] HttpMethods =
    {
        "POST "u8.ToArray(),
        "GET "u8.ToArray(),
        "HEAD "u8.ToArray(),
        "PUT "u8.ToArray(),
        "OPTIONS "u8.ToArray(),
        "DELETE "u8.ToArray(),
        "PATCH "u8.ToArray(),
    };

    public static PrintClassification Classify(ReadOnlySpan<byte> head)
    {
        if (LooksLikeHttp(head)) return PrintClassification.Http;

        // prefijos < 8 chars ASCII upper o espacio podrian ser HTTP truncado: esperar mas bytes
        if (head.Length < 8)
        {
            var allAsciiUpperOrSpace = true;
            foreach (var b in head)
            {
                if (!((b >= (byte)'A' && b <= (byte)'Z') || b == (byte)' '))
                {
                    allAsciiUpperOrSpace = false;
                    break;
                }
            }
            if (allAsciiUpperOrSpace) return PrintClassification.Indeterminate;
        }
        return PrintClassification.Raw;
    }

    private static bool LooksLikeHttp(ReadOnlySpan<byte> head)
    {
        foreach (var method in HttpMethods)
        {
            if (head.Length >= method.Length && head[..method.Length].SequenceEqual(method))
                return true;
        }
        return false;
    }
}
