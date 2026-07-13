namespace PacCollector.Domain.Errors;

public sealed class SampleNotFoundException : DomainException
{
    public string Uuid { get; }

    public SampleNotFoundException(string uuid)
        : base($"sample \"{uuid}\" not found")
    {
        Uuid = uuid;
    }
}
