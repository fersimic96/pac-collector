using FluentAssertions;
using PacCollector.Domain.Errors;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Tests.ValueObjects;

public class AnalyzerSerialTests
{
    [Fact]
    public void AcceptsValidSerial()
    {
        AnalyzerSerial.Create("2125").AsString.Should().Be("2125");
    }

    [Fact]
    public void TrimsWhitespace()
    {
        AnalyzerSerial.Create("  2125  ").AsString.Should().Be("2125");
    }

    [Fact]
    public void RejectsEmpty()
    {
        Action a = () => AnalyzerSerial.Create("");
        Action b = () => AnalyzerSerial.Create("   ");
        a.Should().Throw<InvalidAnalyzerSerialException>();
        b.Should().Throw<InvalidAnalyzerSerialException>();
    }

    [Fact]
    public void RejectsPathSeparators()
    {
        Action a = () => AnalyzerSerial.Create("2125/foo");
        Action b = () => AnalyzerSerial.Create("2125\\bar");
        a.Should().Throw<InvalidAnalyzerSerialException>();
        b.Should().Throw<InvalidAnalyzerSerialException>();
    }
}
