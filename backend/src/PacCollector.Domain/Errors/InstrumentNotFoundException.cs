namespace PacCollector.Domain.Errors;

public sealed class InstrumentNotFoundException : DomainException
{
    public string Serial { get; }

    public InstrumentNotFoundException(string serial)
        : base($"instrument \"{serial}\" not found")
    {
        Serial = serial;
    }
}
