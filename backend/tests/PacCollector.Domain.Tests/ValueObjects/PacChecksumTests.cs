using FluentAssertions;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Tests.ValueObjects;

public class PacChecksumTests
{
    [Fact]
    public void EmptyString_Yields_0000()
    {
        PacChecksum.FromString("").AsString.Should().Be("0000");
    }

    [Fact]
    public void SingleByteA_Yields_00BF()
    {
        // 'A' = 0x41 → ((0x41 ^ 0xFF) + 1) & 0xFF = 0xBF → "00BF"
        PacChecksum.FromString("A").AsString.Should().Be("00BF");
    }

    [Fact]
    public void Format_IsAlways4UppercaseHex()
    {
        var cs = PacChecksum.FromString("hello world").AsString;
        cs.Should().HaveLength(4);
        cs.Should().MatchRegex("^[0-9A-F]{4}$");
    }

    [Fact]
    public void SameInput_YieldsSameChecksum()
    {
        var a = PacChecksum.FromString("payload-XYZ");
        var b = PacChecksum.FromString("payload-XYZ");
        a.Should().Be(b);
    }

    [Fact]
    public void DifferentInputs_YieldDifferentChecksums()
    {
        var a = PacChecksum.FromString("OptiPMD");
        var b = PacChecksum.FromString("OptiPMD ");
        a.Should().NotBe(b);
    }
}
