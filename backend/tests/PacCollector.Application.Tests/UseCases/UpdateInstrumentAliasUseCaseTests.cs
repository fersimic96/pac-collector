using NSubstitute;
using PacCollector.Application.UseCases;
using PacCollector.Domain.Ports;

namespace PacCollector.Application.Tests.UseCases;

public class UpdateInstrumentAliasUseCaseTests
{
    [Fact]
    public async Task Execute_TrimsAndPassesAlias()
    {
        var repo = Substitute.For<IInstrumentRepository>();
        var sut = new UpdateInstrumentAliasUseCase(repo);

        await sut.ExecuteAsync("2125", "  DESTILA-1  ");

        await repo.Received(1).UpdateAliasAsync("2125", "DESTILA-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_EmptyOrWhitespace_PassesNull()
    {
        var repo = Substitute.For<IInstrumentRepository>();
        var sut = new UpdateInstrumentAliasUseCase(repo);

        await sut.ExecuteAsync("2125", "   ");
        await repo.Received(1).UpdateAliasAsync("2125", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NullAlias_PassesNull()
    {
        var repo = Substitute.For<IInstrumentRepository>();
        var sut = new UpdateInstrumentAliasUseCase(repo);

        await sut.ExecuteAsync("2125", null);
        await repo.Received(1).UpdateAliasAsync("2125", null, Arg.Any<CancellationToken>());
    }
}
