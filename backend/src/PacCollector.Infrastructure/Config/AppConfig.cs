namespace PacCollector.Infrastructure.Config;

public sealed class AppConfig
{
    public const uint CurrentVersion = 1;

    public uint Version { get; set; } = CurrentVersion;
    public GeneralSettings General { get; set; } = new();
    public OutputFormats OutputFormats { get; set; } = new();
    public Dictionary<string, InstrumentSettings> Instruments { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, InstrumentRoute> InstrumentRoutes { get; set; } = new(StringComparer.Ordinal);

    // deep clone para que Snapshot del ConfigStore no comparta referencias mutables
    public AppConfig Clone()
    {
        var copy = new AppConfig
        {
            Version = Version,
            General = General.Clone(),
            OutputFormats = OutputFormats.Clone(),
            Instruments = new Dictionary<string, InstrumentSettings>(StringComparer.Ordinal),
            InstrumentRoutes = new Dictionary<string, InstrumentRoute>(StringComparer.Ordinal),
        };
        foreach (var (k, v) in Instruments)
            copy.Instruments[k] = v.Clone();
        foreach (var (k, v) in InstrumentRoutes)
            copy.InstrumentRoutes[k] = v.Clone();
        return copy;
    }

    public IReadOnlyList<string> Validate()
    {
        var errs = new List<string>();
        if (string.IsNullOrEmpty(General.Delimiter))
            errs.Add("general.delimiter no puede estar vacío");
        if (General.RecentKeep == 0)
            errs.Add("general.recent_keep debe ser >= 1");
        if (General.PrintPort == 0)
            errs.Add("general.print_port debe ser > 0");

        ValidatePath("db_dir", General.DbDir, errs, "general");
        ValidatePath("recent_dir", General.RecentDir, errs, "general");

        foreach (var (analyzerType, inst) in Instruments)
        {
            ValidatePath("output_dir", inst.OutputDir, errs, $"instruments[{analyzerType}]");
            ValidatePath("recent_dir", inst.RecentDir, errs, $"instruments[{analyzerType}]");
        }

        return errs;
    }

    private static void ValidatePath(string key, string? value, List<string> errs, string scope)
    {
        if (value is null) return;
        if (string.IsNullOrWhiteSpace(value))
        {
            errs.Add($"{scope}.{key} no puede estar vacío");
            return;
        }
        if (IsUnsafePath(value))
            errs.Add($"{scope}.{key} apunta a una ruta de sistema peligrosa: {value}");
    }

    private static bool IsUnsafePath(string p)
    {
        var normalized = p.Trim().TrimEnd('/');
        return normalized is "" or "/" or "/etc" or "/sys" or "/proc" or "/dev"
            or "/Library" or "/System" or "/Applications" or "/Users"
            or "C:\\" or "C:\\Windows" or "C:\\Program Files";
    }
}
