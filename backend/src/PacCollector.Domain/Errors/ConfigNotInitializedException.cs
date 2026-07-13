namespace PacCollector.Domain.Errors;

public sealed class ConfigNotInitializedException : DomainException
{
    public ConfigNotInitializedException()
        : base("config not initialized — db_dir and recent_dir must be set") { }
}
