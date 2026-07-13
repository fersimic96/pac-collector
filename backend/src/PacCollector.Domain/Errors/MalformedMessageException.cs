namespace PacCollector.Domain.Errors;

public sealed class MalformedMessageException : DomainException
{
    public string Reason { get; }

    public MalformedMessageException(string reason)
        : base($"malformed instrument message: {reason}")
    {
        Reason = reason;
    }
}
