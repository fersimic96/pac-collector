using FluentAssertions;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Domain.Tests.ValueObjects;

public class SafeFilenameTests
{
    [Fact]
    public void ReplacesPathSeparators()
    {
        SafeFilename.Sanitize("a/b\\c").AsString.Should().Be("a_b_c");
    }

    [Fact]
    public void CollapsesDoubleSpaceAndReplacesWithUnderscore()
    {
        SafeFilename.Sanitize("foo  bar").AsString.Should().Be("foo_bar");
    }

    [Fact]
    public void HandlesRealOptiPmdPattern()
    {
        var combined = "25 Apr 2026_14:45 ";
        SafeFilename.Sanitize(combined).AsString.Should().Be("25_Apr_2026_14_45");
    }
}
