using PacCollector.Domain.Errors;

namespace PacCollector.Domain.ValueObjects;

public readonly struct AnalyzerSerial : IEquatable<AnalyzerSerial>
{
    private readonly string _value;

    private AnalyzerSerial(string value) => _value = value;

    public static AnalyzerSerial Create(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidAnalyzerSerialException("empty", string.Empty);
        if (trimmed.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new InvalidAnalyzerSerialException("contains forbidden chars (/, \\, NUL)", trimmed);
        return new AnalyzerSerial(trimmed);
    }

    public string AsString => _value ?? string.Empty;

    public override string ToString() => AsString;

    public bool Equals(AnalyzerSerial other) => string.Equals(AsString, other.AsString, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is AnalyzerSerial o && Equals(o);
    public override int GetHashCode() => AsString.GetHashCode(StringComparison.Ordinal);
    public static bool operator ==(AnalyzerSerial a, AnalyzerSerial b) => a.Equals(b);
    public static bool operator !=(AnalyzerSerial a, AnalyzerSerial b) => !a.Equals(b);
}
