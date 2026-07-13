using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Hotfolder;

namespace PacCollector.Infrastructure.Tests.Hotfolder;

public class HotfolderTemplateRendererTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 14, 30, 0, TimeSpan.Zero);

    private static Sample MakeSample(
        double? ibp = 50.5,
        double? fbp = 200.7,
        string? op = "Fer",
        string? prog = "ASTM D7345",
        IEnumerable<CurvePoint>? curve = null,
        IDictionary<string, string>? extra = null) => new()
    {
        Uuid = Guid.NewGuid().ToString(),
        Serial = AnalyzerSerial.Create("1216"),
        AnalyzerType = "OptiPMD",
        SampleIdentifier = "S-001",
        Operator = op,
        Program = prog,
        StartAt = T0,
        EndAt = T0.AddMinutes(10),
        Ibp = ibp,
        Fbp = fbp,
        Residue = 1.3,
        Recovery = 98.1,
        Curve = curve is null ? DistillationCurve.Empty() : DistillationCurve.Create(curve.ToList()),
        Extra = new SortedDictionary<string, string>(extra ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        ReceivedAt = T0,
    };

    private static HotfolderTemplate MakeTemplate(params string[] lines)
        => new() { Name = "t", FilenameTemplate = "{Serial}.txt", LineEnding = "LF", Lines = new(lines) };

    [Fact]
    public void Render_SubstitutesBasicTokens()
    {
        var tpl = MakeTemplate("serial={Serial}", "ibp={Ibp:F2}", "fbp={Fbp}");
        var r = HotfolderTemplateRenderer.Render(tpl, MakeSample(), alias: null);
        r.Body.Should().Contain("serial=1216");
        r.Body.Should().Contain("ibp=50.50");
        r.Body.Should().Contain("fbp=200.7");
    }

    [Fact]
    public void Render_AliasFallback()
    {
        var tpl = MakeTemplate("Instrument: {Alias|AnalyzerType}");

        // sin alias -> usa AnalyzerType
        var noAlias = HotfolderTemplateRenderer.Render(tpl, MakeSample(), alias: null);
        noAlias.Body.Should().Contain("Instrument: OptiPMD");

        // con alias -> usa alias
        var withAlias = HotfolderTemplateRenderer.Render(tpl, MakeSample(), alias: "PAC-LAB-1");
        withAlias.Body.Should().Contain("Instrument: PAC-LAB-1");
    }

    [Fact]
    public void Render_NullFallbackUsesLiteral()
    {
        var s = MakeSample();
        s.Ibp = null;
        var tpl = MakeTemplate("IBP: {Ibp:R|NaN}");
        var r = HotfolderTemplateRenderer.Render(tpl, s, alias: null);
        r.Body.Should().Contain("IBP: NaN");
    }

    [Fact]
    public void Render_ExtraFallbackChain()
    {
        // primer extra existe -> usa ese
        var withGroup = MakeSample(extra: new Dictionary<string, string> { ["Group"] = "5" });
        var tpl = MakeTemplate("Group: {Extra.Group|Extra.group|1}");
        var r1 = HotfolderTemplateRenderer.Render(tpl, withGroup, alias: null);
        r1.Body.Should().Contain("Group: 5");

        // primer extra no existe, segundo si
        var withLower = MakeSample(extra: new Dictionary<string, string> { ["group"] = "3" });
        var r2 = HotfolderTemplateRenderer.Render(tpl, withLower, alias: null);
        r2.Body.Should().Contain("Group: 3");

        // ninguno existe -> literal "1"
        var none = MakeSample();
        var r3 = HotfolderTemplateRenderer.Render(tpl, none, alias: null);
        r3.Body.Should().Contain("Group: 1");
    }

    [Fact]
    public void Render_ConditionalLineSkipsWhenPathIsNull()
    {
        var withOp = MakeSample(op: "Fer");
        var noOp = MakeSample(op: null);
        var tpl = MakeTemplate("?{Operator}?OperatorId;{Operator}");

        var r1 = HotfolderTemplateRenderer.Render(tpl, withOp, alias: null);
        r1.Body.Should().Contain("OperatorId;Fer");

        var r2 = HotfolderTemplateRenderer.Render(tpl, noOp, alias: null);
        r2.Body.Should().NotContain("OperatorId");
    }

    [Fact]
    public void Render_CurveForEachExpandsToRows()
    {
        var curve = new[]
        {
            new CurvePoint(5, 70.0),
            new CurvePoint(10, 80.5),
            new CurvePoint(95, 195.0),
        };
        var s = MakeSample(curve: curve);
        var tpl = MakeTemplate("header", "{Curve.ForEach:{PctLabel} {TemperatureC:R}}", "footer");
        var r = HotfolderTemplateRenderer.Render(tpl, s, alias: null);

        r.Body.Should().Contain("5% 70");
        r.Body.Should().Contain("10% 80.5");
        r.Body.Should().Contain("95% 195");
        // header y footer alrededor
        var lines = r.Body.Split('\n');
        Array.IndexOf(lines, "header").Should().BeLessThan(Array.IndexOf(lines, "5% 70"));
        Array.IndexOf(lines, "footer").Should().BeGreaterThan(Array.IndexOf(lines, "95% 195"));
    }

    [Fact]
    public void Render_PctPadded4InCurveContext()
    {
        var curve = new[]
        {
            new CurvePoint(5, 70.0),
            new CurvePoint(95, 195.0),
        };
        var s = MakeSample(curve: curve);
        var tpl = MakeTemplate("{Curve.ForEach:Recovered_{PctPadded4};{TemperatureC:R}}");
        var r = HotfolderTemplateRenderer.Render(tpl, s, alias: null);

        r.Body.Should().Contain("Recovered_0005;70");
        r.Body.Should().Contain("Recovered_0095;195");
    }

    [Fact]
    public void Render_ExtraForEachIteratesEntries()
    {
        var extra = new Dictionary<string, string>
        {
            ["FirmwareVersion"] = "3.02",
            ["HeadSN"] = "24 I8 M0044",
            ["hpgl_curve"] = "<binary blob>",  // se excluye
        };
        var s = MakeSample(extra: extra);
        var tpl = MakeTemplate("{Extra.ForEach:{Key};{Value}}");
        var r = HotfolderTemplateRenderer.Render(tpl, s, alias: null);

        r.Body.Should().Contain("FirmwareVersion;3.02");
        r.Body.Should().Contain("HeadSN;24 I8 M0044");
        r.Body.Should().NotContain("hpgl_curve");  // explicitly excluded
    }

    [Fact]
    public void Render_FilenameTemplateGetsSubstituted()
    {
        var tpl = new HotfolderTemplate
        {
            Name = "t",
            FilenameTemplate = "{Serial}_{SampleIdentifier}_{StartAt:yyyyMMdd}.txt",
            LineEnding = "LF",
            Lines = new() { "body" },
        };
        var r = HotfolderTemplateRenderer.Render(tpl, MakeSample(), alias: null);
        r.Filename.Should().Be("1216_S-001_20260501.txt");
    }

    [Fact]
    public void Render_LineEndingCrlfUsesCrLf()
    {
        var tpl = new HotfolderTemplate
        {
            Name = "t", FilenameTemplate = "x.txt", LineEnding = "CRLF",
            Lines = new() { "a", "b" },
        };
        var r = HotfolderTemplateRenderer.Render(tpl, MakeSample(), alias: null);
        r.Body.Should().Be("a\r\nb\r\n");
    }

    [Fact]
    public void Render_LineEndingLfUsesLf()
    {
        var tpl = new HotfolderTemplate
        {
            Name = "t", FilenameTemplate = "x.txt", LineEnding = "LF",
            Lines = new() { "a", "b" },
        };
        var r = HotfolderTemplateRenderer.Render(tpl, MakeSample(), alias: null);
        r.Body.Should().Be("a\nb\n");
    }

    [Fact]
    public void Render_BuiltinLimsEthernetTemplateProducesExpectedShape()
    {
        // smoke test contra el template embedded real, no parity bit-a-bit
        var template = HotfolderTemplateLoader.LoadAll().Single(t => t.Name == "lims-ethernet-txt");
        var curve = new[] { new CurvePoint(5, 70.0), new CurvePoint(95, 195.0) };
        var s = MakeSample(curve: curve);

        var r = HotfolderTemplateRenderer.Render(template, s, alias: "LAB-1");

        r.Body.Should().Contain("Status: C\r\n");
        r.Body.Should().Contain("Instrument: LAB-1\r\n");
        r.Body.Should().Contain("Probe serial number: 1216\r\n");
        r.Body.Should().Contain("Sample: S-001\r\n");
        r.Body.Should().Contain("IBP 50.5\r\n");
        r.Body.Should().Contain("5% 70\r\n");
        r.Body.Should().Contain("95% 195\r\n");
        r.Body.Should().Contain("FBP 200.7\r\n");
    }

    [Fact]
    public void Render_BuiltinCsvAllTemplateProducesKeyValueLines()
    {
        var template = HotfolderTemplateLoader.LoadAll().Single(t => t.Name == "csv-all");
        var s = MakeSample();

        var r = HotfolderTemplateRenderer.Render(template, s, alias: "LAB-1");

        r.Body.Should().Contain("Key;Value\r\n");
        r.Body.Should().Contain("Instrument;LAB-1\r\n");
        r.Body.Should().Contain("AnalyzerSN;1216\r\n");
        r.Body.Should().Contain("SampleId;S-001\r\n");
        r.Body.Should().Contain("IBP;50.5\r\n");
        r.Body.Should().Contain("FBP;200.7\r\n");
    }
}
