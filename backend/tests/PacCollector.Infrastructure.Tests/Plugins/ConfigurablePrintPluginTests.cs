using FluentAssertions;
using PacCollector.Domain.Errors;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Infrastructure.Tests.Plugins;

public class ConfigurablePrintPluginTests
{
    private static DateTimeOffset Now => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static byte[] LoadFixture(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", filename);
        return File.ReadAllBytes(path);
    }

    private static PrintPluginSpec FindSpec(string id)
        => PrintPluginSpecLoader.LoadAll().Single(s => s.Id == id);

    [Fact]
    public void Loader_LoadsAllEmbeddedSpecs()
    {
        var specs = PrintPluginSpecLoader.LoadAll();
        specs.Should().HaveCount(4);
        specs.Select(s => s.AnalyzerType).Should()
            .BeEquivalentTo(new[] { "OptiFZP", "OptiCPP", "OptiPMD", "OptiDist2" });
    }

    [Fact]
    public void FzpPlugin_AcceptsOnlyFzpPayload()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optifzp-print-builtin"));
        plugin.AcceptsPrintFormat(LoadFixture("optifzp_print_8076.bin")).Should().BeTrue();
        plugin.AcceptsPrintFormat(LoadFixture("opticpp_print_8035.bin")).Should().BeFalse();
        plugin.AcceptsPrintFormat(LoadFixture("optipmd_print_1216.bin")).Should().BeFalse();
    }

    [Fact]
    public void CppPlugin_AcceptsOnlyCppPayload()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("opticpp-print-builtin"));
        plugin.AcceptsPrintFormat(LoadFixture("opticpp_print_8035.bin")).Should().BeTrue();
        plugin.AcceptsPrintFormat(LoadFixture("optifzp_print_8076.bin")).Should().BeFalse();
        plugin.AcceptsPrintFormat(LoadFixture("optipmd_print_1216.bin")).Should().BeFalse();
    }

    [Fact]
    public void PmdPlugin_AcceptsOnlyPmdPayload()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optipmd-print-builtin"));
        plugin.AcceptsPrintFormat(LoadFixture("optipmd_print_1216.bin")).Should().BeTrue();
        plugin.AcceptsPrintFormat(LoadFixture("optifzp_print_8076.bin")).Should().BeFalse();
        plugin.AcceptsPrintFormat(LoadFixture("opticpp_print_8035.bin")).Should().BeFalse();
    }

    [Fact]
    public void Fzp_ParsesFixtureToSampleWithSerialAndKind()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optifzp-print-builtin"));
        var s = plugin.ParsePrintMessage(LoadFixture("optifzp_print_8076.bin"), sourceIp: "10.0.0.1", Now);

        s.AnalyzerType.Should().Be("OptiFZP");
        s.Serial.AsString.Should().Be("8076");
        s.Curve.IsEmpty.Should().BeTrue();  // FZP es LabelValue, no tiene curva
        s.SourceIp.Should().Be("10.0.0.1");
        s.ReceivedAt.Should().Be(Now);
    }

    [Fact]
    public void Cpp_ParsesFixtureToSampleWithSerialAndKind()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("opticpp-print-builtin"));
        var s = plugin.ParsePrintMessage(LoadFixture("opticpp_print_8035.bin"), sourceIp: null, Now);

        s.AnalyzerType.Should().Be("OptiCPP");
        s.Serial.AsString.Should().Be("8035");
        s.Curve.IsEmpty.Should().BeTrue();  // CPP es LabelValue
    }

    [Fact]
    public void Pmd_ParsesFixtureWithDistillationTable()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optipmd-print-builtin"));
        var s = plugin.ParsePrintMessage(LoadFixture("optipmd_print_1216.bin"), sourceIp: null, Now);

        s.AnalyzerType.Should().Be("OptiPMD");
        s.Serial.AsString.Should().Be("1216");
        s.Ibp.Should().NotBeNull("PMD print debe extraer IBP de la tabla de destilacion");
        s.Fbp.Should().NotBeNull("PMD print debe extraer FBP de la tabla de destilacion");
        s.Curve.IsEmpty.Should().BeFalse("PMD print debe tener al menos un punto en la curva");
    }

    [Fact]
    public void Pmd_DescriptionsIncludeFieldsAndCommon()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optipmd-print-builtin"));
        plugin.FieldDescriptions.Should().ContainKey("FirmwareVersion");
        plugin.FieldDescriptions.Should().ContainKey("HeadSN");
        plugin.FieldDescriptions.Should().ContainKey("AtmPrs");
    }

    [Fact]
    public void ParseMessage_OnPrintPlugin_ThrowsMalformed()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optifzp-print-builtin"));
        var act = () => plugin.ParseMessage(new byte[] { 0x7B }, null, Now);
        act.Should().Throw<MalformedMessageException>()
           .WithMessage("*only handles print-mode payloads*");
    }

    [Fact]
    public void Plugin_WithoutHeaderMarker_Throws()
    {
        var plugin = new ConfigurablePrintPlugin(FindSpec("optifzp-print-builtin"));
        var raw = System.Text.Encoding.UTF8.GetBytes("nothing here that looks like an OptiFZP header");
        var act = () => plugin.ParsePrintMessage(raw, null, Now);
        act.Should().Throw<MalformedMessageException>();
    }

    [Fact]
    public void IsPrintPlugin_IsTrueForAllSpecs()
    {
        foreach (var spec in PrintPluginSpecLoader.LoadAll())
        {
            var plugin = new ConfigurablePrintPlugin(spec);
            plugin.IsPrintPlugin.Should().BeTrue();
        }
    }
}
