using PacCollector.Application.Dtos;
using PacCollector.Domain.Ports;

namespace PacCollector.Application.UseCases;

public sealed class ListSamplesUseCase
{
    private readonly ISampleRepository _samples;

    public ListSamplesUseCase(ISampleRepository samples) => _samples = samples;

    public async Task<SamplePage> ExecuteAsync(
        SampleFiltersInput filters,
        uint offset,
        uint limit,
        CancellationToken ct = default)
    {
        var domainFilters = new SampleQueryFilters(
            Serial: filters.Serial,
            Program: filters.Program,
            Operator: filters.Operator,
            From: filters.From,
            To: filters.To);

        var entities = await _samples.ListPaginatedAsync(domainFilters, offset, limit, ct);
        var total = await _samples.CountAsync(domainFilters, ct);
        return new SamplePage(
            Items: entities.Select(SampleOutputDto.FromEntity).ToList(),
            Total: total,
            Offset: offset,
            Limit: limit);
    }
}
