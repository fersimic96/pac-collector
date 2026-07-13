namespace PacCollector.Domain.Errors;

public sealed class NoPluginForTypeException : DomainException
{
    public string AnalyzerType { get; }

    public NoPluginForTypeException(string analyzerType)
        : base($"no plugin registered for analyzer type \"{analyzerType}\"")
    {
        AnalyzerType = analyzerType;
    }
}
