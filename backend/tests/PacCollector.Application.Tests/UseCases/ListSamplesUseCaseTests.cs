using FluentAssertions;
using NSubstitute;
using PacCollector.Application.Dtos;
using PacCollector.Application.UseCases;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Application.Tests.UseCases;

public class ListSamplesUseCaseTests
{
    [Fact]
    public async Task Execute_PassesFiltersAndPaging_ToRepo()
    {
        var repo = Substitute.For<ISampleRepository>();
        repo.ListPaginatedAsync(Arg.Any<SampleQueryFilters>(), 10u, 25u, Arg.Any<CancellationToken>())
            .Returns(new List<Sample>());
        repo.CountAsync(Arg.Any<SampleQueryFilters>(), Arg.Any<CancellationToken>())
            .Returns(0UL);
        var sut = new ListSamplesUseCase(repo);
        var filters = new SampleFiltersInput(Serial: "2125", Program: "ASTM");

        var page = await sut.ExecuteAsync(filters, offset: 10, limit: 25);

        page.Offset.Should().Be(10u);
        page.Limit.Should().Be(25u);
        page.Total.Should().Be(0UL);
        await repo.Received(1).ListPaginatedAsync(
            Arg.Is<SampleQueryFilters>(f => f.Serial == "2125" && f.Program == "ASTM"),
            10u, 25u, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_MapsResultsToDtos()
    {
        var repo = Substitute.For<ISampleRepository>();
        var sample = new Sample
        {
            Uuid = "u-1",
            Serial = AnalyzerSerial.Create("2125"),
            AnalyzerType = "OptiPMD",
            SampleIdentifier = "29",
            Ibp = 142.7,
            Fbp = 366.9,
            Curve = DistillationCurve.Empty(),
            ReceivedAt = DateTimeOffset.UtcNow,
        };
        repo.ListPaginatedAsync(Arg.Any<SampleQueryFilters>(), 0u, 50u, Arg.Any<CancellationToken>())
            .Returns(new List<Sample> { sample });
        repo.CountAsync(Arg.Any<SampleQueryFilters>(), Arg.Any<CancellationToken>())
            .Returns(1UL);

        var page = await new ListSamplesUseCase(repo).ExecuteAsync(new SampleFiltersInput(), 0, 50);

        page.Total.Should().Be(1UL);
        page.Items.Should().HaveCount(1);
        page.Items[0].Uuid.Should().Be("u-1");
        page.Items[0].Ibp.Should().Be(142.7);
    }
}
