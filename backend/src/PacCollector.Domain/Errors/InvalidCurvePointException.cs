namespace PacCollector.Domain.Errors;

public sealed class InvalidCurvePointException : DomainException
{
    public string Reason { get; }

    public InvalidCurvePointException(string reason)
        : base($"invalid curve point: {reason}")
    {
        Reason = reason;
    }
}
