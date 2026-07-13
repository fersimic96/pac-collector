namespace PacCollector.Domain.Errors;

public sealed class ConfigInvalidException : DomainException
{
    public string Field { get; }
    public string Reason { get; }

    public ConfigInvalidException(string field, string reason)
        : base($"config invalid: {field}: {reason}")
    {
        Field = field;
        Reason = reason;
    }
}
