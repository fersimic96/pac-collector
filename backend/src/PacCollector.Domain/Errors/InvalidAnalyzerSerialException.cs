namespace PacCollector.Domain.Errors;

public sealed class InvalidAnalyzerSerialException : DomainException
{
    public string Reason { get; }
    public string Received { get; }

    public InvalidAnalyzerSerialException(string reason, string received)
        : base($"invalid analyzer serial: {reason} (received: \"{received}\")")
    {
        Reason = reason;
        Received = received;
    }
}
