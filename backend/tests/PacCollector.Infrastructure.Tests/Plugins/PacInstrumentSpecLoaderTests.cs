using FluentAssertions;
using PacCollector.Infrastructure.Plugins.Builtin;

namespace PacCollector.Infrastructure.Tests.Plugins;

// mismo contrato de resiliencia que PrintPluginSpecLoader: bad JSON en override
// dir nunca crashea boot. Test simetrico para evitar regresion del bug en cualquiera.
public class PacInstrumentSpecLoaderTests
{
    [Fact]
    public void LoadAll_NoOverrideDir_ReturnsEmbeddedOnly()
    {
        var specs = PacInstrumentSpecLoader.LoadAll();
        specs.Should().NotBeEmpty();
        specs.Select(s => s.AnalyzerType).Should().Contain("OptiPMD");
    }

    [Fact]
    public void LoadAll_NonexistentOverrideDir_DoesNotThrow()
    {
        var fake = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"));
        var act = () => PacInstrumentSpecLoader.LoadAll(fake);
        act.Should().NotThrow();
    }

    [Fact]
    public void LoadAll_OverrideDirWithBadJson_SkipsBadFile_KeepsEmbedded()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not valid");

            var specs = PacInstrumentSpecLoader.LoadAll(dir);

            specs.Should().NotBeEmpty();
            specs.Should().NotContain(s => s.PluginId == "broken");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void LoadAll_OverrideDirWithIncompleteSpec_SkipsFile()
    {
        var dir = CreateTempDir();
        try
        {
            // sin pluginId ni analyzerType — Validate rechaza
            File.WriteAllText(Path.Combine(dir, "incomplete.json"), """{ "displayName": "no id" }""");

            var specs = PacInstrumentSpecLoader.LoadAll(dir);

            specs.Should().NotBeEmpty();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-lims-loader-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
