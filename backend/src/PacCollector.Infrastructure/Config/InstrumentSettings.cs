namespace PacCollector.Infrastructure.Config;

public sealed class InstrumentSettings
{
    public bool Enabled { get; set; } = true;
    public string? Alias { get; set; }
    public string? OutputDir { get; set; }
    public string? RecentDir { get; set; }
    public bool? ShowKey { get; set; }
    public bool? ShowUnit { get; set; }
    public List<string>? SelectedParameters { get; set; }
    public string? HotFolderDir { get; set; }
    public HotFolderFormat? HotFolderFormat { get; set; }

    // referencia a un HotfolderTemplate por Name. Si esta seteado se usa en lugar
    // del HotFolderFormat enum (cuya rama queda como fallback legacy).
    public string? HotFolderTemplate { get; set; }

    public InstrumentSettings Clone() => new()
    {
        Enabled = Enabled,
        Alias = Alias,
        OutputDir = OutputDir,
        RecentDir = RecentDir,
        ShowKey = ShowKey,
        ShowUnit = ShowUnit,
        SelectedParameters = SelectedParameters is null ? null : new List<string>(SelectedParameters),
        HotFolderDir = HotFolderDir,
        HotFolderFormat = HotFolderFormat,
        HotFolderTemplate = HotFolderTemplate,
    };
}
