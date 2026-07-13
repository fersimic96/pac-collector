using FluentAssertions;
using PacCollector.Infrastructure.Hotfolder;

namespace PacCollector.Infrastructure.Tests.Hotfolder;

public class HotfolderTemplateLoaderTests
{
    [Fact]
    public void LoadAll_EmbeddedOnly_ReturnsThreeBuiltins()
    {
        var templates = HotfolderTemplateLoader.LoadAll();
        templates.Should().HaveCount(3);
        templates.Select(t => t.Name).Should()
            .BeEquivalentTo(new[] { "lims-ethernet-txt", "csv-all", "curve-csv" });
    }

    [Fact]
    public void LoadAll_NonexistentOverrideDir_DoesNotThrow_ReturnsEmbedded()
    {
        var fake = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"));
        var templates = HotfolderTemplateLoader.LoadAll(fake);
        templates.Should().HaveCount(3);
    }

    [Fact]
    public void LoadAll_OverrideDirWithBadJson_SkipsBadFile_KeepsEmbedded()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");

            var templates = HotfolderTemplateLoader.LoadAll(dir);

            templates.Should().HaveCount(3);
            templates.Should().NotContain(t => t.Name == "broken");
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void LoadAll_OverrideDirWithValidNewTemplate_PicksItUp()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "my-custom.json"), """
            {
              "name": "my-custom",
              "filenameTemplate": "{Serial}.txt",
              "lineEnding": "LF",
              "lines": ["Serial,{Serial}"]
            }
            """);

            var templates = HotfolderTemplateLoader.LoadAll(dir);

            templates.Should().HaveCount(4);
            templates.Should().Contain(t => t.Name == "my-custom");
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void LoadAll_OverrideDirCanReplaceEmbedded()
    {
        var dir = CreateTempDir();
        try
        {
            // override "csv-all" con un template trivial
            File.WriteAllText(Path.Combine(dir, "csv-all-override.json"), """
            {
              "name": "csv-all",
              "filenameTemplate": "override.csv",
              "lineEnding": "LF",
              "lines": ["overridden!"]
            }
            """);

            var templates = HotfolderTemplateLoader.LoadAll(dir);
            var csvAll = templates.Single(t => t.Name == "csv-all");

            csvAll.FilenameTemplate.Should().Be("override.csv");
            csvAll.Lines.Should().ContainSingle().Which.Should().Be("overridden!");
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void LoadFromFile_StrictMode_ThrowsOnInvalidJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "invalid-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{not json");
        try
        {
            var act = () => HotfolderTemplateLoader.LoadFromFile(path);
            act.Should().Throw<Exception>();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void LoadFromFile_ValidatesRequiredFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-name-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{ "filenameTemplate": "x.txt", "lines": ["a"] }""");
        try
        {
            var act = () => HotfolderTemplateLoader.LoadFromFile(path);
            act.Should().Throw<Exception>().WithMessage("*name is required*");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void LoadFromFile_RejectsInvalidLineEnding()
    {
        var path = Path.Combine(Path.GetTempPath(), "bad-eol-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
        {
          "name": "x",
          "filenameTemplate": "x.txt",
          "lineEnding": "WTF",
          "lines": ["a"]
        }
        """);
        try
        {
            var act = () => HotfolderTemplateLoader.LoadFromFile(path);
            act.Should().Throw<Exception>().WithMessage("*lineEnding*");
        }
        finally { TryDelete(path); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hotfolder-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }
}
