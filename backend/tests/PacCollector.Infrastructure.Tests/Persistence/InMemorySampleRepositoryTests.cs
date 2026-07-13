using FluentAssertions;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;
using PacCollector.Infrastructure.Persistence;

namespace PacCollector.Infrastructure.Tests.Persistence;

public class InMemorySampleRepositoryTests
{
    private static Sample Make(string uuid, string serial, string program, DateTimeOffset received) => new()
    {
        Uuid = uuid,
        Serial = AnalyzerSerial.Create(serial),
        AnalyzerType = "OptiPMD",
        SampleIdentifier = uuid,
        Program = program,
        Curve = DistillationCurve.Empty(),
        ReceivedAt = received,
    };

    [Fact]
    public async Task SaveAndFindByUuid()
    {
        var repo = new InMemorySampleRepository();
        var s = Make("u-1", "2125", "ASTM", DateTimeOffset.UtcNow);
        await repo.SaveReceivedSampleAsync(s);
        (await repo.FindByUuidAsync("u-1")).Should().NotBeNull();
        (await repo.FindByUuidAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task ExistsForRunMatchesAllThreeKeys()
    {
        var repo = new InMemorySampleRepository();
        var s = Make("u-1", "2125", "ASTM", DateTimeOffset.UtcNow);
        s.StartAt = DateTimeOffset.UnixEpoch;
        await repo.SaveReceivedSampleAsync(s);

        (await repo.ExistsForRunAsync("2125", "u-1", DateTimeOffset.UnixEpoch)).Should().BeTrue();
        (await repo.ExistsForRunAsync("2125", "u-1", DateTimeOffset.UnixEpoch.AddSeconds(1))).Should().BeFalse();
        (await repo.ExistsForRunAsync("9999", "u-1", DateTimeOffset.UnixEpoch)).Should().BeFalse();
    }

    [Fact]
    public async Task ListPaginatedAppliesFiltersAndOrdersDescByReceivedAt()
    {
        var repo = new InMemorySampleRepository();
        var t = DateTimeOffset.UtcNow;
        await repo.SaveReceivedSampleAsync(Make("a", "2125", "ASTM", t.AddSeconds(-10)));
        await repo.SaveReceivedSampleAsync(Make("b", "2125", "ASTM", t.AddSeconds(-5)));
        await repo.SaveReceivedSampleAsync(Make("c", "2125", "OTHER", t));
        await repo.SaveReceivedSampleAsync(Make("d", "9999", "ASTM", t));

        var page = await repo.ListPaginatedAsync(new SampleQueryFilters(Serial: "2125", Program: "ASTM"), 0, 50);

        page.Should().HaveCount(2);
        page[0].Uuid.Should().Be("b"); // most recent first
        page[1].Uuid.Should().Be("a");
    }

    [Fact]
    public async Task CountRespectsFilters()
    {
        var repo = new InMemorySampleRepository();
        var t = DateTimeOffset.UtcNow;
        await repo.SaveReceivedSampleAsync(Make("a", "2125", "ASTM", t));
        await repo.SaveReceivedSampleAsync(Make("b", "2125", "ASTM", t));
        await repo.SaveReceivedSampleAsync(Make("c", "9999", "ASTM", t));

        (await repo.CountAsync(new SampleQueryFilters(Serial: "2125"))).Should().Be(2UL);
        (await repo.CountAsync(new SampleQueryFilters())).Should().Be(3UL);
    }

    [Fact]
    public async Task CountReceivedSinceFiltersByDate()
    {
        var repo = new InMemorySampleRepository();
        var t = DateTimeOffset.UtcNow;
        await repo.SaveReceivedSampleAsync(Make("a", "2125", "ASTM", t.AddDays(-2)));
        await repo.SaveReceivedSampleAsync(Make("b", "2125", "ASTM", t));

        (await repo.CountReceivedSinceAsync(t.AddDays(-1))).Should().Be(1UL);
        (await repo.CountReceivedSinceAsync(t.AddDays(-10))).Should().Be(2UL);
    }
}
