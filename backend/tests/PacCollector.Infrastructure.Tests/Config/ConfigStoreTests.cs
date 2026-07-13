using FluentAssertions;
using PacCollector.Infrastructure.Config;

namespace PacCollector.Infrastructure.Tests.Config;

public class ConfigStoreTests
{
    [Fact]
    public void LoadReturnsDefaultsWhenFileMissing()
    {
        using var td = new TempDir();
        var store = ConfigStore.Load(td.File("settings.json"));
        store.Snapshot().Version.Should().Be(AppConfig.CurrentVersion);
    }

    [Fact]
    public void LoadBacksUpCorruptedFile()
    {
        using var td = new TempDir();
        var path = td.File("settings.json");
        File.WriteAllText(path, "{this is not json");

        _ = ConfigStore.Load(path);

        File.Exists(path).Should().BeFalse("corrupt settings.json should have been renamed");
        Directory.GetFiles(td.Path).Should().Contain(p => p.Contains("broken"));
    }

    [Fact]
    public void ReplaceWritesAtomicallyAndUpdatesSnapshot()
    {
        using var td = new TempDir();
        var path = td.File("settings.json");
        var store = ConfigStore.Load(path);

        var c = new AppConfig();
        c.General.PrintPort = 9100;
        store.Replace(c);

        File.Exists(path).Should().BeTrue();
        var onDisk = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(
            File.ReadAllText(path), JsonOptions.Default)!;
        onDisk.General.PrintPort.Should().Be((ushort)9100);
        store.Snapshot().General.PrintPort.Should().Be((ushort)9100);
    }

    [Fact]
    public void ParseRejectsFutureVersion()
    {
        using var td = new TempDir();
        var path = td.File("settings.json");
        File.WriteAllText(path, """{"version":999,"general":{}}""");

        _ = ConfigStore.Load(path);

        File.Exists(path).Should().BeFalse("future version should be treated as corrupt and backed up");
    }

    [Fact]
    public void ReplaceTriggersChangedEvent()
    {
        using var td = new TempDir();
        var store = ConfigStore.Load(td.File("settings.json"));
        AppConfig? observed = null;
        store.Changed += (_, cfg) => observed = cfg;

        var newCfg = new AppConfig();
        newCfg.General.SelectedIp = "192.168.1.10";
        store.Replace(newCfg);

        observed.Should().NotBeNull();
        observed!.General.SelectedIp.Should().Be("192.168.1.10");
    }
}
