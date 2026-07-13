// ══════════════════════════════════════════════════════════════════════════════
// VALUE OBJECT PacChecksum — el checksum del protocolo PAC.
// ★ ESTE ES EL ALGORITMO QUE SACAMOS DEL DLL PROPIETARIO (decompilando con ILSpy). ★
// El equipo PAC rechaza la respuesta si el checksum no coincide EXACTAMENTE, así que
// tuvimos que reproducir su cálculo bit por bit y validarlo con muestras reales.
//
// "Value Object" = un objeto que representa un VALOR (no una entidad con identidad).
// Dos checksums con el mismo texto son "iguales", como dos billetes de $100.
// ══════════════════════════════════════════════════════════════════════════════
// LEYENDA:  [C#] = palabra del lenguaje   [.NET] = de Microsoft   [NUESTRO] = nuestro
// ══════════════════════════════════════════════════════════════════════════════
namespace PacCollector.Domain.ValueObjects;

// struct [C#] = como una class, pero es un "tipo por valor" (liviano, se copia al pasarlo).
//   Ideal para valores chicos e inmutables como este.
// readonly [C#] = el struct entero es inmutable: una vez creado, no cambia.
// IEquatable<PacChecksum> [.NET] = "prometo saber compararme con otro PacChecksum".
public readonly struct PacChecksum : IEquatable<PacChecksum>
{
    private readonly string _value;   // private [C#] = solo accesible adentro. Guarda el checksum como texto "XXXX".

    // Constructor PRIVADO: nadie crea un PacChecksum con "new" desde afuera.
    // Solo se crea a través de FromBytes/FromString (abajo). Esto garantiza que
    // el valor siempre pase por el algoritmo correcto.
    private PacChecksum(string value) => _value = value;

    // ─── EL ALGORITMO (reverse-engineered del DLL) ───────────────────────────
    // static [C#] = se llama sin tener un objeto: PacChecksum.FromBytes(...)
    // ReadOnlySpan<byte> [.NET] = una "ventana" de bytes eficiente (sin copiar memoria)
    public static PacChecksum FromBytes(ReadOnlySpan<byte> input)
    {
        uint sum = 0;                       // uint [C#] = entero sin signo. Acumulador.
        foreach (var b in input)            // foreach [C#] = recorrer cada byte del mensaje
            sum = (sum + b) & 0xFF;         // sumar el byte y quedarse con los últimos 8 bits (& 0xFF = "mod 256")
        var result = ((sum ^ 0xFF) + 1) & 0xFF;  // complemento a dos: invertir bits (^ 0xFF) + 1, y recortar a 8 bits
        return new PacChecksum($"{result:X4}");  // {result:X4} [C#] = formatear como hex de 4 dígitos (ej "00A3")
    }

    // atajo: calcular el checksum de un texto (lo pasa a bytes UTF-8 y llama al de arriba)
    public static PacChecksum FromString(string input)
        => FromBytes(System.Text.Encoding.UTF8.GetBytes(input));
    // └─ System.Text.Encoding.UTF8 [.NET]  .  GetBytes() [.NET] — texto → bytes

    // devuelve el checksum como texto. Si nunca se seteó (_value null), devuelve "0000".
    public string AsString => _value ?? "0000";   // ?? [C#] = "si es null, usá 0000"

    public override string ToString() => AsString;  // override [C#] = redefine el ToString() heredado

    // ─── IGUALDAD POR VALOR ───────────────────────────────────────────────────
    // Todo esto le enseña a C# a comparar dos checksums por su texto, no por referencia.
    public bool Equals(PacChecksum other) => string.Equals(AsString, other.AsString, StringComparison.Ordinal);
    // └─ string.Equals(..., Ordinal) [.NET] = comparación de texto exacta, carácter por carácter

    public override bool Equals(object? obj) => obj is PacChecksum o && Equals(o);
    // └─ "is PacChecksum o" [C#] = "¿obj es un PacChecksum? si sí, guardalo en o"

    public override int GetHashCode() => AsString.GetHashCode(StringComparison.Ordinal);
    // └─ GetHashCode [.NET] = número para usarlo en diccionarios/sets

    // operadores == y != para poder escribir "checksum1 == checksum2"
    public static bool operator ==(PacChecksum a, PacChecksum b) => a.Equals(b);   // operator [C#]
    public static bool operator !=(PacChecksum a, PacChecksum b) => !a.Equals(b);
}
