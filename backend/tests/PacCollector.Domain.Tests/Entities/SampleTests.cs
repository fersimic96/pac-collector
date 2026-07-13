using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Tests.Entities;

public class SampleTests
{
    private static DateTimeOffset T0 => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Sample MakeSample() => new()
    {
        Uuid = "abc-123",
        Serial = AnalyzerSerial.Create("2125"),
        AnalyzerType = "OptiPMD",
        SampleIdentifier = "29",
        Operator = "LUCAS",
        Program = "ASTM D7345#",
        StartAt = T0,
        EndAt = T0,
        Ibp = 142.7,
        Fbp = 366.9,
        Residue = 1.8,
        Recovery = 97.8,
        FbpVolume = 97.0,
        EndOfTest = true,
        AlarmBitmask = 0UL,
        Curve = DistillationCurve.Empty(),
        SourceIp = "192.168.50.10",
        ReceivedAt = T0,
        RawJson = "{}",
    };

    [Fact]
    public void IsCompleteWhenEndOfTestTrue()
    {
        MakeSample().IsComplete().Should().BeTrue();
    }

    [Fact]
    public void HasAlarmsWhenBitmaskNonzero()
    {
        var s = MakeSample();
        s.AlarmBitmask = 0x1000UL;
        s.HasAlarms().Should().BeTrue();
    }

    [Fact]
    public void NoAlarmsWhenZeroOrNull()
    {
        var s = MakeSample();
        s.AlarmBitmask = 0UL;
        s.HasAlarms().Should().BeFalse();
        s.AlarmBitmask = null;
        s.HasAlarms().Should().BeFalse();
    }
}
