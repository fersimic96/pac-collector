using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Filesystem;

namespace PacCollector.ParityTests;

// gate de paridad para los formatos de archivo que el LIMS lee. Cualquier cambio
// en los formatos LIMS Ethernet TXT / LimsClassic / CurveCsv / MasterRow debe ser
// intencional - si rompe, el test falla y se revisa el diff.
public class FileWriterFormatsParityTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Sample MakeOptiPmdSample() => new()
    {
        Uuid = "u-pmd-1",
        Serial = AnalyzerSerial.Create("2125"),
        AnalyzerType = "OptiPMD",
        SampleIdentifier = "LAB-001",
        Operator = "LUIS",
        Program = "ASTM D86",
        StartAt = T0,
        EndAt = T0.AddMinutes(45),
        Ibp = 35.2,
        Fbp = 215.8,
        Residue = 1.2,
        Recovery = 97.5,
        FbpVolume = 96.0,
        EndOfTest = true,
        Curve = DistillationCurve.Create(new[]
        {
            new CurvePoint(5.0, 52.1),
            new CurvePoint(50.0, 130.4),
            new CurvePoint(95.0, 205.4),
        }),
        SourceIp = "192.168.1.10",
        ReceivedAt = T0,
        RawJson = "",
    };

    [Fact]
    public void LimsEthernetTxt_OptiPmd_MatchesSnapshot()
    {
        var text = FileWriterImpl.LimsEthernetTxt(MakeOptiPmdSample(), alias: "DESTILA-1");
        SnapshotComparer.AssertMatchesTextSnapshot(text, "lims_ethernet_optipmd");
    }

    [Fact]
    public void LimsClassicText_OptiPmd_MatchesSnapshot()
    {
        var text = FileWriterImpl.LimsClassicText(MakeOptiPmdSample(), delimiter: ";", eol: "", showKey: true, _showUnit: false);
        SnapshotComparer.AssertMatchesTextSnapshot(text, "lims_classic_optipmd");
    }

    [Fact]
    public void CurveCsv_OptiPmd_MatchesSnapshot()
    {
        var text = FileWriterImpl.CurveCsv(MakeOptiPmdSample());
        SnapshotComparer.AssertMatchesTextSnapshot(text, "curve_csv_optipmd");
    }

    [Fact]
    public void SampleAllCsv_OptiPmd_MatchesSnapshot()
    {
        var text = FileWriterImpl.SampleAllCsv(MakeOptiPmdSample(), alias: "DESTILA-1");
        SnapshotComparer.AssertMatchesTextSnapshot(text, "sample_all_csv_optipmd");
    }

    [Fact]
    public void MasterRow_OptiPmd_MatchesSnapshot()
    {
        var row = FileWriterImpl.MasterRow(MakeOptiPmdSample());
        SnapshotComparer.AssertMatchesTextSnapshot(row, "master_row_optipmd");
    }
}
