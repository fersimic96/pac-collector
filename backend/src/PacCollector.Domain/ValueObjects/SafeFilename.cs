using System.Text;

namespace PacCollector.Domain.ValueObjects;

public readonly struct SafeFilename : IEquatable<SafeFilename>
{
    private static readonly HashSet<char> Forbidden =
        [':', '/', '\\', '*', '?', '"', '<', '>', '|', '\n', '\r', '\t'];

    private readonly string _value;

    private SafeFilename(string value) => _value = value;

    public static SafeFilename Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return new SafeFilename(string.Empty);

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
            sb.Append(Forbidden.Contains(c) ? '_' : c);

        var s = sb.ToString();
        while (s.Contains("  ", StringComparison.Ordinal))
            s = s.Replace("  ", " ", StringComparison.Ordinal);

        s = s.Trim().Replace(' ', '_');
        return new SafeFilename(s);
    }

    public string AsString => _value ?? string.Empty;
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public override string ToString() => AsString;

    public bool Equals(SafeFilename other) => string.Equals(AsString, other.AsString, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is SafeFilename o && Equals(o);
    public override int GetHashCode() => AsString.GetHashCode(StringComparison.Ordinal);
    public static bool operator ==(SafeFilename a, SafeFilename b) => a.Equals(b);
    public static bool operator !=(SafeFilename a, SafeFilename b) => !a.Equals(b);
}
