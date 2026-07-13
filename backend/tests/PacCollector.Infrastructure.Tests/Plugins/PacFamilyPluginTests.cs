using System.Text;
using FluentAssertions;
using PacCollector.Domain.Errors;
using PacCollector.Infrastructure.Plugins;
using PacCollector.Infrastructure.Plugins.Builtin;

namespace PacCollector.Infrastructure.Tests.Plugins;

public class PacFamilyPluginTests
{
    private static DateTimeOffset Now => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void RegistersAllPacInstruments()
    {
        BuiltinSpecs.All.Should().HaveCount(7);
        var types = BuiltinSpecs.All.Select(s => s.AnalyzerType).ToList();
        types.Should().Contain(new[] { "OptiPMD", "OptiCPP", "OptiFPP", "OptiFZP", "OptiMPP", "OptiMVD", "OptiFuel" });
    }

    [Fact]
    public void OptiPmdParsesRealPayload()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiPmd);
        var raw = Utf8("""
        {
            "AnalyzerType": "OptiPMD",
            "DataDictionary": {
                "AnalyzerSerialNumber": "2125",
                "SampleIdentifier": "29",
                "OperatorId": "LUCAS",
                "IBP": " 142.7",
                "FBP": " 366.9",
                "Recovered_0050": " 232.5"
            }
        }
        """);

        var s = plugin.ParseMessage(raw, sourceIp: null, receivedAt: Now);

        s.Serial.AsString.Should().Be("2125");
        s.AnalyzerType.Should().Be("OptiPMD");
        s.SampleIdentifier.Should().Be("29");
        s.Operator.Should().Be("LUCAS");
        s.Ibp.Should().Be(142.7);
        s.Fbp.Should().Be(366.9);
        s.Curve.Count.Should().Be(1);
        s.Curve.Points[0].PctRecovered.Should().Be(50.0);
        s.Curve.Points[0].TemperatureC.Should().Be(232.5);
    }

    [Fact]
    public void OptiCppParsesPayloadWithoutDistillationFields()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiCpp);
        var raw = Utf8("""
        {
            "AnalyzerType": "OptiCPP",
            "DataDictionary": {
                "AnalyzerSerialNumber": "5001",
                "SampleIdentifier": "diesel-A",
                "OperatorId": "FER",
                "CloudpointResult": "-12.5",
                "Cloudpoint_EndOfTest": "1"
            }
        }
        """);

        var s = plugin.ParseMessage(raw, sourceIp: null, receivedAt: Now);

        s.Serial.AsString.Should().Be("5001");
        s.AnalyzerType.Should().Be("OptiCPP");
        s.Ibp.Should().BeNull();
        s.Extra.Should().ContainKey("CloudpointResult").WhoseValue.Should().Be("-12.5");
    }

    [Fact]
    public void DescriptionsIncludeCommonAndSpecific()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiMvd);
        plugin.FieldDescriptions.Should().ContainKey("OperatorId");
        plugin.FieldDescriptions.Should().ContainKey("DynamicViscosity");
    }

    [Fact]
    public void InvalidJsonThrowsMalformed()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiPmd);
        var raw = Utf8("{this is not json");

        Action act = () => plugin.ParseMessage(raw, null, Now);
        act.Should().Throw<MalformedMessageException>();
    }

    [Fact]
    public void MissingSerialThrowsMalformed()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiPmd);
        var raw = Utf8("""{"AnalyzerType":"OptiPMD","DataDictionary":{}}""");

        Action act = () => plugin.ParseMessage(raw, null, Now);
        act.Should().Throw<MalformedMessageException>()
            .WithMessage("*AnalyzerSerialNumber*");
    }

    [Fact]
    public void ParsesPacDateTimeFromStartRunFields()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiPmd);
        var raw = Utf8("""
        {
            "DataDictionary": {
                "AnalyzerSerialNumber": "2125",
                "StartRunDate": "25 Apr 2026",
                "StartRunTime": "14:45 ",
                "EndRunDate": "25 Apr 2026",
                "EndRunTime": "14:55"
            }
        }
        """);

        var s = plugin.ParseMessage(raw, null, Now);

        s.StartAt.Should().NotBeNull();
        s.StartAt!.Value.Year.Should().Be(2026);
        s.StartAt.Value.Month.Should().Be(4);
        s.StartAt.Value.Day.Should().Be(25);
        s.StartAt.Value.Hour.Should().Be(14);
        s.StartAt.Value.Minute.Should().Be(45);
        s.EndAt.Should().NotBeNull();
        s.EndAt!.Value.Minute.Should().Be(55);
    }

    [Fact]
    public void ExtraFieldsExcludeReservedAndCurvePoints()
    {
        var plugin = new PacFamilyPlugin(BuiltinSpecs.OptiPmd);
        var raw = Utf8("""
        {
            "DataDictionary": {
                "AnalyzerSerialNumber": "2125",
                "OperatorId": "FER",
                "IBP": "100",
                "Recovered_0005": "120",
                "RandomExtraKey": " some_value "
            }
        }
        """);

        var s = plugin.ParseMessage(raw, null, Now);

        s.Extra.Should().NotContainKey("AnalyzerSerialNumber");
        s.Extra.Should().NotContainKey("OperatorId");
        s.Extra.Should().NotContainKey("IBP");
        s.Extra.Should().NotContainKey("Recovered_0005");
        s.Extra.Should().ContainKey("RandomExtraKey").WhoseValue.Should().Be("some_value");
    }
}
