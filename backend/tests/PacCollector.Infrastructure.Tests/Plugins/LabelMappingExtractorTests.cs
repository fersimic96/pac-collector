using FluentAssertions;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Infrastructure.Tests.Plugins;

// el extractor dispatcha entre label-based (default) y regex (cuando Pattern esta seteado).
// estos tests validan la mecanica del dispatch + cleanup de valores (CleanValue, °C suffix).
public class LabelMappingExtractorTests
{
    [Fact]
    public void Extract_WithoutPattern_FallsBackToLabelBased()
    {
        var mapping = new PrintLabelMapping { Label = "Flash Point", Key = "FlashPoint" };
        const string text = "Operator: J.Perez\nFlash Point: 64.5\nMethod: ASTM D93";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().Be("64.5");
    }

    [Fact]
    public void Extract_WithPattern_UsesRegex()
    {
        var mapping = new PrintLabelMapping
        {
            Label = "ignored-when-pattern-set",
            Key = "RunNumber",
            Pattern = @"Run #(\d+)",
            Group = 1,
        };
        const string text = "Sample done\nRun #4271 completed\nNext: idle";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().Be("4271");
    }

    [Fact]
    public void Extract_WithPattern_UsesDefaultGroupOne()
    {
        var mapping = new PrintLabelMapping
        {
            Label = "x",
            Key = "Cal",
            Pattern = @"Calibration:\s+([\w-]+)",
            // Group not set → default 1
        };
        const string text = "Calibration: 2026-04-25-A\n";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().Be("2026-04-25-A");
    }

    [Fact]
    public void Extract_WithPattern_NoMatch_ReturnsNull()
    {
        var mapping = new PrintLabelMapping
        {
            Label = "x",
            Key = "Y",
            Pattern = @"NotInText:\s+(\S+)",
        };
        const string text = "totally unrelated content here";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().BeNull();
    }

    [Fact]
    public void Extract_WithInvalidPattern_ReturnsNullInsteadOfThrowing()
    {
        var mapping = new PrintLabelMapping
        {
            Label = "x",
            Key = "Y",
            Pattern = @"[unclosed",  // regex invalido
        };
        const string text = "anything";

        // no debe romper el procesamiento — solo loggear via Console.Error?
        // (en CLI 'spec test' se detecta porque coverage reporta unmatched)
        var act = () => LabelMappingExtractor.Extract(mapping, text);

        act.Should().NotThrow();
        LabelMappingExtractor.Extract(mapping, text).Should().BeNull();
    }

    [Fact]
    public void Extract_WithPattern_GroupOutOfRange_ReturnsNull()
    {
        var mapping = new PrintLabelMapping
        {
            Label = "x",
            Key = "Y",
            Pattern = @"(\d+)",
            Group = 5,  // solo hay group 0 (full match) y group 1
        };
        const string text = "123";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().BeNull();
    }

    [Fact]
    public void Extract_LabelBased_StripsTrailingCelsius()
    {
        var mapping = new PrintLabelMapping { Label = "Result", Key = "R" };
        // PAC reports usan "232.5 C" — el wrapper debe quitar el sufijo
        const string text = "Result: 232.5 C\n";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().Be("232.5");
    }

    [Fact]
    public void Extract_RegexBased_StripsTrailingCelsius()
    {
        var mapping = new PrintLabelMapping
        {
            Label = "x",
            Key = "Y",
            Pattern = @"FP\s+result:\s+(\S+\s+C)",
        };
        const string text = "FP result: 64.0 C";

        var value = LabelMappingExtractor.Extract(mapping, text);

        value.Should().Be("64.0");
    }

    [Fact]
    public void Extract_EmptyLabel_AndNoPattern_ReturnsNull()
    {
        var mapping = new PrintLabelMapping { Label = "", Key = "X" };
        const string text = "anything: value";

        LabelMappingExtractor.Extract(mapping, text).Should().BeNull();
    }
}
