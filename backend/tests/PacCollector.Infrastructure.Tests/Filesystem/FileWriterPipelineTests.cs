using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Config;
using PacCollector.Infrastructure.Filesystem;

namespace PacCollector.Infrastructure.Tests.Filesystem;

public class FileWriterPipelineTests
{
    private static DateTimeOffset T0 => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Sample MakeOptiPmdSample(string uuid = "u-1") => new()
    {
        Uuid = uuid,
        Serial = AnalyzerSerial.Create("2125"),
        AnalyzerType = "OptiPMD",
        SampleIdentifier = "LAB-001",
        Operator = "LUIS",
        Program = "ASTM D86",
        StartAt = T0,
        EndAt = T0,
        Ibp = 35.2, Fbp = 215.8, Residue = 1.2, Recovery = 97.5, FbpVolume = 96.0,
        EndOfTest = true,
        Curve = DistillationCurve.Create(new[]
        {
            new CurvePoint(5.0, 52.1),
            new CurvePoint(95.0, 205.4),
        }),
        SourceIp = "192.168.1.10",
        ReceivedAt = T0,
        RawJson = """{"AnalyzerType":"OptiPMD"}""",
    };

    private static Sample MakeOptiCppSample()
    {
        var s = MakeOptiPmdSample("cpp-1");
        s.AnalyzerType = "OptiCPP";
        s.Serial = AnalyzerSerial.Create("8035");
        s.Curve = DistillationCurve.Empty();
        s.Ibp = null; s.Fbp = null;
        s.Extra = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["CloudPoint"] = "-5.7",
        };
        return s;
    }

    private static (FileWriterImpl writer, string dbDir, string recentDir, ConfigStore cfgStore) NewWriter(TempDir td)
    {
        var dbDir = td.File("db");
        var recentDir = td.File("recent");
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(recentDir);
        var cfgPath = td.File("settings.json");
        var cfgStore = ConfigStore.Load(cfgPath);
        var writer = new FileWriterImpl(dbDir, recentDir, cfgStore);
        return (writer, dbDir, recentDir, cfgStore);
    }

    [Fact]
    public async Task OptiPmd_WritesFullArchiveTree()
    {
        using var td = new TempDir();
        var (writer, dbDir, _, _) = NewWriter(td);

        await writer.WriteSampleArtifactsAsync(MakeOptiPmdSample());

        var bucket = Path.Combine(dbDir, "2125_OptiPMD");
        Directory.Exists(Path.Combine(bucket, "json")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(bucket, "json"), "*.json").Should().NotBeEmpty();
        Directory.GetFiles(Path.Combine(bucket, "samples"), "*.txt").Should().NotBeEmpty();
        Directory.GetFiles(Path.Combine(bucket, "reports"), "*.legible.txt").Should().NotBeEmpty();
        Directory.GetFiles(Path.Combine(bucket, "curves"), "*.curva.csv").Should().NotBeEmpty();
        File.Exists(Path.Combine(bucket, "master.csv")).Should().BeTrue();
        File.Exists(Path.Combine(dbDir, "_global", "master.csv")).Should().BeTrue();
    }

    [Fact]
    public async Task OptiCpp_DoesNotWriteLimsTxt_NoCurveCsv()
    {
        using var td = new TempDir();
        var (writer, dbDir, _, _) = NewWriter(td);

        await writer.WriteSampleArtifactsAsync(MakeOptiCppSample());

        var bucket = Path.Combine(dbDir, "8035_OptiCPP");
        // samples/.txt es solo destilación → no se debe crear file
        var samplesDir = Path.Combine(bucket, "samples");
        if (Directory.Exists(samplesDir))
            Directory.GetFiles(samplesDir, "*.txt").Should().BeEmpty();
        // curve.csv requiere curva, CPP no tiene
        var curvesDir = Path.Combine(bucket, "curves");
        if (Directory.Exists(curvesDir))
            Directory.GetFiles(curvesDir, "*.curva.csv").Should().BeEmpty();
        // json y reports sí
        File.Exists(Path.Combine(bucket, "master.csv")).Should().BeTrue();
    }

    [Fact]
    public async Task HotFolderRoute_PerSerial_CsvAll_WritesPlainCsv()
    {
        using var td = new TempDir();
        var hotFolder = td.File("lims-inbox");
        Directory.CreateDirectory(hotFolder);

        var (writer, _, _, cfgStore) = NewWriter(td);
        var cfg = new AppConfig();
        cfg.InstrumentRoutes["2125"] = new InstrumentRoute
        {
            HotFolderFormat = HotFolderFormat.CsvAll,
            HotFolderDir = hotFolder,
            Alias = "DESTILA-1",
        };
        cfgStore.Replace(cfg);

        await writer.WriteSampleArtifactsAsync(MakeOptiPmdSample());

        var csvFiles = Directory.GetFiles(hotFolder, "*.csv");
        csvFiles.Should().HaveCount(1);
        var content = await File.ReadAllTextAsync(csvFiles[0]);
        content.Should().Contain("Key;Value\r\n");
        content.Should().Contain("Instrument;DESTILA-1");
        content.Should().Contain("AnalyzerSN;2125");
        // sin subfolders
        Directory.GetDirectories(hotFolder).Should().BeEmpty();
    }

    [Fact]
    public async Task HotFolderRoute_LimsEthernet_SkippedForNonOptiPmd()
    {
        using var td = new TempDir();
        var hotFolder = td.File("lims-inbox");
        Directory.CreateDirectory(hotFolder);

        var (writer, _, _, cfgStore) = NewWriter(td);
        var cfg = new AppConfig();
        cfg.InstrumentRoutes["8035"] = new InstrumentRoute
        {
            HotFolderFormat = HotFolderFormat.LimsEthernet,
            HotFolderDir = hotFolder,
        };
        cfgStore.Replace(cfg);

        await writer.WriteSampleArtifactsAsync(MakeOptiCppSample());

        Directory.GetFiles(hotFolder).Should().BeEmpty(
            "LimsEthernet hot folder format aplica solo a OptiPMD");
    }

    [Fact]
    public async Task MasterCsv_AppendsRowAcrossMultipleSamples()
    {
        using var td = new TempDir();
        var (writer, dbDir, _, _) = NewWriter(td);

        var s1 = MakeOptiPmdSample("a"); s1.SampleIdentifier = "S-1";
        var s2 = MakeOptiPmdSample("b"); s2.SampleIdentifier = "S-2";
        await writer.WriteSampleArtifactsAsync(s1);
        await writer.WriteSampleArtifactsAsync(s2);

        var masterPath = Path.Combine(dbDir, "_global", "master.csv");
        var lines = await File.ReadAllLinesAsync(masterPath);
        // header + 2 rows
        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("timestamp,serial,analyzerType,sampleId");
        lines[1].Should().Contain("S-1");
        lines[2].Should().Contain("S-2");
    }

    [Fact]
    public async Task WriteUnknownPayload_SavesToInvalidBucketForNonUtf8()
    {
        using var td = new TempDir();
        var (writer, dbDir, _, _) = NewWriter(td);

        var rawBytes = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC };
        var result = await writer.WriteUnknownPayloadAsync(
            rawBytes, analyzerType: null, sourceIp: "192.168.1.99",
            reason: "test", receivedAt: T0);

        var invalidDir = Path.Combine(dbDir, "_unknown", "_invalid");
        Directory.Exists(invalidDir).Should().BeTrue();
        Directory.GetFiles(invalidDir, "*.bin").Should().NotBeEmpty();
        Directory.GetFiles(invalidDir, "*.meta.json").Should().NotBeEmpty();
        result.Path.Should().Contain("_invalid");
    }

    [Fact]
    public async Task WriteUnknownPayload_TypedBucketWhenAnalyzerTypeProvided()
    {
        using var td = new TempDir();
        var (writer, dbDir, _, _) = NewWriter(td);

        var raw = """{"AnalyzerType":"OptiUnknown"}"""u8.ToArray();
        await writer.WriteUnknownPayloadAsync(
            raw, analyzerType: "OptiUnknown", sourceIp: "192.168.1.99",
            reason: "no plugin", receivedAt: T0);

        Directory.Exists(Path.Combine(dbDir, "_unknown", "OptiUnknown")).Should().BeTrue();
    }
}
