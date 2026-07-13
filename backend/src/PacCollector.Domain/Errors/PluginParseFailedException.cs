namespace PacCollector.Domain.Errors;

public sealed class PluginParseFailedException : DomainException
{
    public string Reason { get; }

    public PluginParseFailedException(string reason)
        : base($"plugin parse failed: {reason}")
    {
        Reason = reason;
    }
}
