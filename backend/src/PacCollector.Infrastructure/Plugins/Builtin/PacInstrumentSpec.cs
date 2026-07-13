namespace PacCollector.Infrastructure.Plugins.Builtin;

public sealed class PacFieldSpec
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Group { get; set; } = "";

    public PacFieldSpec() { }
    public PacFieldSpec(string key, string label, string unit, string group)
    {
        Key = key;
        Label = label;
        Unit = unit;
        Group = group;
    }
}

// spec declarativo de un equipo PAC (LIMS JSON Ethernet). Se carga desde JSON externo.
// para agregar un equipo nuevo basta con dejar un .json en plugins/lims/ del DataDir.
public sealed class PacInstrumentSpec
{
    public string PluginId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AnalyzerType { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Version { get; set; } = "";
    public List<PacFieldSpec> FieldSpecs { get; set; } = new();

    public PacInstrumentSpec() { }
    public PacInstrumentSpec(
        string pluginId,
        string displayName,
        string analyzerType,
        string vendor,
        string version,
        IReadOnlyList<PacFieldSpec> fieldSpecs)
    {
        PluginId = pluginId;
        DisplayName = displayName;
        AnalyzerType = analyzerType;
        Vendor = vendor;
        Version = version;
        FieldSpecs = fieldSpecs.ToList();
    }
}
