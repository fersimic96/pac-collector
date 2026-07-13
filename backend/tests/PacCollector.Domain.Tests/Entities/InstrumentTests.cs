using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Tests.Entities;

public class InstrumentTests
{
    private static DateTimeOffset T0 => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Instrument MakeInstrument()
        => Instrument.NewDiscovered(
            AnalyzerSerial.Create("2125"),
            "OptiPMD",
            "192.168.50.10",
            T0);

    [Fact]
    public void NewDiscoveredSetsInitialState()
    {
        var i = MakeInstrument();
        i.Serial.AsString.Should().Be("2125");
        i.TotalSamples.Should().Be(0UL);
        i.Enabled.Should().BeTrue();
        i.Alias.Should().BeNull();
    }

    [Fact]
    public void IsOnlineTrueWithinThreshold()
    {
        var i = MakeInstrument();
        var later = T0.AddSeconds(30);
        i.IsOnline(later, TimeSpan.FromSeconds(60)).Should().BeTrue();
    }

    [Fact]
    public void IsOnlineFalseAfterThreshold()
    {
        var i = MakeInstrument();
        var later = T0.AddSeconds(120);
        i.IsOnline(later, TimeSpan.FromSeconds(60)).Should().BeFalse();
    }

    [Fact]
    public void DisplayNameUsesAliasWhenSet()
    {
        var i = MakeInstrument();
        i.SetAlias("Distill-1");
        i.DisplayName().Should().Be("Distill-1");
    }

    [Fact]
    public void DisplayNameFallsBackToSerialAndType()
    {
        var i = MakeInstrument();
        i.DisplayName().Should().Be("2125 (OptiPMD)");
    }

    [Fact]
    public void SetAliasTrimsAndRejectsEmpty()
    {
        var i = MakeInstrument();
        i.SetAlias("  Lab Bench A  ");
        i.Alias.Should().Be("Lab Bench A");
        i.SetAlias("   ");
        i.Alias.Should().BeNull();
    }
}
