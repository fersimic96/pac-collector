using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Errors;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Persistence;

namespace PacCollector.Infrastructure.Tests.Persistence;

public class JsonInstrumentRepositoryTests
{
    private static DateTimeOffset T0 => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static Instrument Make(string sn) => Instrument.NewDiscovered(
        AnalyzerSerial.Create(sn), "OptiPMD", "192.168.1.10", T0);

    [Fact]
    public async Task MissingFileStartsEmpty()
    {
        using var td = new TempDir();
        var repo = JsonInstrumentRepository.Load(td.File("instruments.json"));
        (await repo.ListAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertThenReloadRecoversInstruments()
    {
        using var td = new TempDir();
        var path = td.File("instruments.json");
        {
            var repo = JsonInstrumentRepository.Load(path);
            await repo.UpsertOnContactAsync(Make("8076"));
            await repo.UpsertOnContactAsync(Make("2125"));
        }
        var repo2 = JsonInstrumentRepository.Load(path);
        var all = await repo2.ListAllAsync();
        all.Should().HaveCount(2);
        all[0].Serial.AsString.Should().Be("2125");
        all[1].Serial.AsString.Should().Be("8076");
    }

    [Fact]
    public async Task AliasPersistsAcrossReload()
    {
        using var td = new TempDir();
        var path = td.File("instruments.json");
        {
            var repo = JsonInstrumentRepository.Load(path);
            await repo.UpsertOnContactAsync(Make("8076"));
            await repo.UpdateAliasAsync("8076", "Lab-FZP");
        }
        var repo2 = JsonInstrumentRepository.Load(path);
        var found = await repo2.FindBySerialAsync("8076");
        found.Should().NotBeNull();
        found!.Alias.Should().Be("Lab-FZP");
    }

    [Fact]
    public async Task IncrementSampleCountPersistsAcrossReload()
    {
        using var td = new TempDir();
        var path = td.File("instruments.json");
        {
            var repo = JsonInstrumentRepository.Load(path);
            await repo.UpsertOnContactAsync(Make("8076"));
            await repo.IncrementSampleCountAsync("8076");
            await repo.IncrementSampleCountAsync("8076");
            await repo.IncrementSampleCountAsync("8076");
        }
        var repo2 = JsonInstrumentRepository.Load(path);
        var found = await repo2.FindBySerialAsync("8076");
        found!.TotalSamples.Should().Be(3UL);
    }

    [Fact]
    public async Task LoadRecoversFromCorruptFile()
    {
        using var td = new TempDir();
        var path = td.File("instruments.json");
        File.WriteAllText(path, "{this is not json");

        var repo = JsonInstrumentRepository.Load(path);

        (await repo.ListAllAsync()).Should().BeEmpty();
        File.Exists(path).Should().BeFalse("corrupt file should have been renamed");
        Directory.GetFiles(td.Path).Should().Contain(p => p.Contains("broken"));
    }

    [Fact]
    public async Task UpdateAliasOnUnknownSerialThrows()
    {
        using var td = new TempDir();
        var repo = JsonInstrumentRepository.Load(td.File("instruments.json"));

        Func<Task> act = () => repo.UpdateAliasAsync("9999", "X");

        await act.Should().ThrowAsync<InstrumentNotFoundException>();
    }
}
