namespace PacCollector.Domain.Entities;

public abstract record PluginSource
{
    // discriminador para que el frontend pueda hacer source.kind === "builtin"
    public abstract string Kind { get; }

    public sealed record Builtin : PluginSource
    {
        public override string Kind => "builtin";
    }
    public sealed record External(string Path) : PluginSource
    {
        public override string Kind => "external";
    }
}

public sealed record PluginInfo(
    string Id,
    string DisplayName,
    string Version,
    string Vendor,
    IReadOnlyList<string> SupportedTypes,
    PluginSource Source,
    bool Enabled)
{
    public bool IsBuiltin => Source is PluginSource.Builtin;
}
