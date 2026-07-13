namespace PacCollector.Infrastructure.Config;

public sealed class InstrumentRoute
{
    public HotFolderFormat? HotFolderFormat { get; set; }
    public string? HotFolderDir { get; set; }
    public string? Alias { get; set; }

    // nuevo: referencia a un HotfolderTemplate por Name. Si esta seteado se usa
    // en lugar del HotFolderFormat enum (cuya rama queda como fallback legacy).
    // Compatibilidad backward: routes existentes con HotFolderFormat siguen
    // funcionando sin cambios.
    public string? HotFolderTemplate { get; set; }

    public InstrumentRoute Clone() => new()
    {
        HotFolderFormat = HotFolderFormat,
        HotFolderDir = HotFolderDir,
        Alias = Alias,
        HotFolderTemplate = HotFolderTemplate,
    };
}
