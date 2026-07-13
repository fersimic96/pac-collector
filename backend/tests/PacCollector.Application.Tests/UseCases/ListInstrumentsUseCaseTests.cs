using FluentAssertions;
using NSubstitute;
using PacCollector.Application.UseCases;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Ports;
using PacCollector.Domain.ValueObjects;

namespace PacCollector.Application.Tests.UseCases;

public class ListInstrumentsUseCaseTests
{
    [Fact]
    public async Task Execute_MapsEntitiesToDtos()
    {
        var repo = Substitute.For<IInstrumentRepository>();
        var now = DateTimeOffset.UtcNow;
        var instrument = Instrument.NewDiscovered(
            AnalyzerSerial.Create("2125"), "OptiPMD", "192.168.50.10", now);
        repo.ListAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Instrument> { instrument });
        var sut = new ListInstrumentsUseCase(repo);

        var result = await sut.ExecuteAsync();

        result.Should().HaveCount(1);
        result[0].Serial.Should().Be("2125");
        result[0].AnalyzerType.Should().Be("OptiPMD");
        result[0].LastIp.Should().Be("192.168.50.10");
        result[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_EmptyRepo_ReturnsEmptyList()
    {
        var repo = Substitute.For<IInstrumentRepository>();
        repo.ListAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Instrument>());

        var result = await new ListInstrumentsUseCase(repo).ExecuteAsync();

        result.Should().BeEmpty();
    }
}
