using PacCollector.Application.Dtos;
using PacCollector.Domain.Ports;

namespace PacCollector.Application.UseCases;

public sealed class ListInstrumentsUseCase
{
    private readonly IInstrumentRepository _instruments;

    public ListInstrumentsUseCase(IInstrumentRepository instruments) => _instruments = instruments;

    public async Task<IReadOnlyList<InstrumentOutputDto>> ExecuteAsync(CancellationToken ct = default)
    {
        var entities = await _instruments.ListAllAsync(ct);
        return entities.Select(InstrumentOutputDto.FromEntity).ToList();
    }
}
