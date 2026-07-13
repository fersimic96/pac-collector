namespace PacCollector.Infrastructure.Config;

public sealed class OutputFormats
{
    public bool WriteJson { get; set; } = true;
    public bool WriteLimsTxt { get; set; } = true;
    public bool WriteLegibleTxt { get; set; } = true;
    public bool WriteCurveCsv { get; set; } = true;
    public bool WriteMasterCsv { get; set; } = true;
    public bool WriteGlobalMasterCsv { get; set; } = true;
    public bool MirrorToRecent { get; set; } = true;

    public OutputFormats Clone() => new()
    {
        WriteJson = WriteJson,
        WriteLimsTxt = WriteLimsTxt,
        WriteLegibleTxt = WriteLegibleTxt,
        WriteCurveCsv = WriteCurveCsv,
        WriteMasterCsv = WriteMasterCsv,
        WriteGlobalMasterCsv = WriteGlobalMasterCsv,
        MirrorToRecent = MirrorToRecent,
    };
}
