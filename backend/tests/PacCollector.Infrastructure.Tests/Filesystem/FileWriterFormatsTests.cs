using System.Reflection;
using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Filesystem;

namespace PacCollector.Infrastructure.Tests.Filesystem;

public class FileWriterFormatsTests
{
    // los formats son internal static methods; los invocamos via reflection
    private static readonly MethodInfo SampleAllCsv = typeof(FileWriterImpl)
        .GetMethod("SampleAllCsv", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo LimsEthernetTxt = typeof(FileWriterImpl)
        .GetMethod("LimsEthernetTxt", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static string CsvAll(Sample s, string? alias)
        => (string)SampleAllCsv.Invoke(null, [s, alias])!;
    private static string TxtLims(Sample s, string? alias)
        => (string)LimsEthernetTxt.Invoke(null, [s, alias])!;

    private static DateTimeOffset T0 => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Sample MakeDistillationSample()
    {
        var curve = DistillationCurve.Create(new[]
        {
            new CurvePoint(5.0, 52.1),
            new CurvePoint(10.0, 65.3),
            new CurvePoint(95.0, 205.4),
        });
        return new Sample
        {
            Uuid = "abc-123",
            Serial = AnalyzerSerial.Create("2125"),
            AnalyzerType = "OptiPMD",
            SampleIdentifier = "LAB-001",
            Operator = "LUIS",
            Program = "ASTM D86",
            StartAt = T0,
            EndAt = T0,
            Ibp = 35.2,
            Fbp = 215.8,
            Residue = 1.2,
            Recovery = 97.5,
            FbpVolume = 96.0,
            EndOfTest = true,
            AlarmBitmask = 0UL,
            Curve = curve,
            SourceIp = "192.168.1.10",
            ReceivedAt = T0,
            RawJson = "{}",
        };
    }

    private static Sample MakeFzpSample()
    {
        var extra = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["FreezePoint"] = "-92.1",
            ["Cd"] = "-106.2",
            ["Co"] = "-108.8",
            ["Do"] = "-92.1",
            ["Ending"] = "FZP detected",
        };
        return new Sample
        {
            Uuid = "fzp-1",
            Serial = AnalyzerSerial.Create("8076"),
            AnalyzerType = "OptiFZP",
            SampleIdentifier = "LXV10",
            Operator = "LUIS",
            Program = "ASTM D7153",
            StartAt = T0,
            EndAt = null,
            Ibp = null, Fbp = null, Residue = null, Recovery = null, FbpVolume = null,
            EndOfTest = true,
            AlarmBitmask = null,
            Curve = DistillationCurve.Empty(),
            Extra = extra,
            SourceIp = "192.168.1.18",
            ReceivedAt = T0,
            RawJson = "raw print bytes",
        };
    }

    [Fact]
    public void SampleAllCsv_DumpsEveryFieldLimsStyle()
    {
        var csv = CsvAll(MakeDistillationSample(), alias: null);
        csv.Should().StartWith("Key;Value\r\n");
        foreach (var needle in new[]
        {
            "AnalyzerType;OptiPMD", "AnalyzerSN;2125", "SampleId;LAB-001",
            "OperatorId;LUIS", "ProgramName;ASTM D86",
            "IBP;35.2", "FBP;215.8", "Residue;1.2", "Recovery;97.5",
            "Recovered_0005;52.1", "Recovered_0010;65.3", "Recovered_0095;205.4",
            "SourceIP;192.168.1.10",
        })
            csv.Should().Contain(needle);
    }

    [Fact]
    public void SampleAllCsv_WorksWithoutCurve()
    {
        var csv = CsvAll(MakeFzpSample(), alias: null);
        foreach (var needle in new[]
        {
            "AnalyzerType;OptiFZP", "AnalyzerSN;8076", "SampleId;LXV10",
            "FreezePoint;-92.1", "Cd;-106.2", "Co;-108.8", "Do;-92.1", "Ending;FZP detected",
        })
            csv.Should().Contain(needle);

        csv.Should().NotContain("Recovered_");
        csv.Split('\n').Should().NotContain(l => l.StartsWith("IBP;"));
    }

    [Fact]
    public void SampleAllCsv_EscapesSpecialChars()
    {
        var s = MakeFzpSample();
        s.Extra["WeirdKey"] = "value;with;semicolons";
        s.Extra["Quoted"] = "has \"quotes\" inside";
        var csv = CsvAll(s, alias: null);
        csv.Should().Contain("WeirdKey;\"value;with;semicolons\"");
        csv.Should().Contain("Quoted;\"has \"\"quotes\"\" inside\"");
    }

    [Fact]
    public void SampleAllCsv_UsesAliasAsInstrument()
    {
        var csv = CsvAll(MakeDistillationSample(), alias: "DESTILA-1");
        csv.Should().Contain("Instrument;DESTILA-1");
        csv.Should().Contain("AnalyzerType;OptiPMD");
    }

    [Fact]
    public void SampleAllCsv_InstrumentFallsBackToTypeWithoutAlias()
    {
        var csv = CsvAll(MakeDistillationSample(), alias: null);
        csv.Should().Contain("Instrument;OptiPMD");
    }

    [Fact]
    public void LimsEthernetTxt_UsesAliasAsInstrument()
    {
        var withAlias = TxtLims(MakeDistillationSample(), "DESTILA-1");
        withAlias.Should().Contain("Instrument: DESTILA-1\r\n");
        var without = TxtLims(MakeDistillationSample(), null);
        without.Should().Contain("Instrument: OptiPMD\r\n");
    }
}
