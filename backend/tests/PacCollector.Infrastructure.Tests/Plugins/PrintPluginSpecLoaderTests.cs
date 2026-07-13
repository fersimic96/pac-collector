using FluentAssertions;
using PacCollector.Infrastructure.Plugins.Print;

namespace PacCollector.Infrastructure.Tests.Plugins;

// el loader debe ser tolerante a JSON malo en el override dir: log warning + skip,
// nunca crashea el boot. Embedded resources siguen siendo strict.
public class PrintPluginSpecLoaderTests
{
    [Fact]
    public void LoadAll_NoOverrideDir_ReturnsEmbeddedOnly()
    {
        var specs = PrintPluginSpecLoader.LoadAll();
        specs.Should().NotBeEmpty();
        specs.Select(s => s.AnalyzerType).Should().Contain("OptiPMD");
    }

    [Fact]
    public void LoadAll_NonexistentOverrideDir_DoesNotThrow()
    {
        var fake = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"));
        var act = () => PrintPluginSpecLoader.LoadAll(fake);
        act.Should().NotThrow();
    }

    [Fact]
    public void LoadAll_OverrideDirWithBadJson_SkipsBadFile_KeepsEmbedded()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{not valid json");

            var specs = PrintPluginSpecLoader.LoadAll(dir);

            // los embedded deben seguir cargando aunque el override tenga basura
            specs.Should().NotBeEmpty();
            specs.Should().NotContain(s => s.Id == "broken");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void LoadAll_OverrideDirWithValidSpec_PicksItUp()
    {
        var dir = CreateTempDir();
        try
        {
            var json = """
            {
              "id": "test-print-override",
              "displayName": "Test override",
              "analyzerType": "TestEquipment",
              "vendor": "test",
              "version": "0.0.1",
              "kind": "labelValue",
              "headerMarker": "TestEquipment",
              "headlineLabel": "Test point"
            }
            """;
            File.WriteAllText(Path.Combine(dir, "test-override.json"), json);

            var specs = PrintPluginSpecLoader.LoadAll(dir);

            specs.Should().Contain(s => s.Id == "test-print-override");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void LoadAll_OverrideDirWithMissingRequiredFields_SkipsFile_KeepsEmbedded()
    {
        var dir = CreateTempDir();
        try
        {
            // spec sin id ni analyzerType — ValidateOrThrow rechaza
            var json = """{ "displayName": "no id, no analyzerType" }""";
            File.WriteAllText(Path.Combine(dir, "incomplete.json"), json);

            var specs = PrintPluginSpecLoader.LoadAll(dir);

            specs.Should().NotBeEmpty();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void LoadAll_OverrideDirWithBothGoodAndBadFiles_LoadsGoodSkipsBad()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "garbage");
            File.WriteAllText(Path.Combine(dir, "good.json"), """
            {
              "id": "test-good-override",
              "analyzerType": "GoodEquipment",
              "headerMarker": "Good"
            }
            """);

            var specs = PrintPluginSpecLoader.LoadAll(dir);

            specs.Should().Contain(s => s.Id == "test-good-override");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-loader-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
