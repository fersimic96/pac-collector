using PacCollector.Domain.Entities;

namespace PacCollector.Domain.Ports;

public sealed record SampleQueryFilters(
    string? Serial = null,
    string? Program = null,
    string? Operator = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public interface ISampleRepository
{
    Task SaveReceivedSampleAsync(Sample sample, CancellationToken ct = default);
    Task<Sample?> FindByUuidAsync(string uuid, CancellationToken ct = default);

    Task<bool> ExistsForRunAsync(
        string serial,
        string sampleIdentifier,
        DateTimeOffset? startAt,
        CancellationToken ct = default);

    Task<IReadOnlyList<Sample>> ListPaginatedAsync(
        SampleQueryFilters filters,
        uint offset,
        uint limit,
        CancellationToken ct = default);

    Task<ulong> CountAsync(SampleQueryFilters filters, CancellationToken ct = default);
    Task<ulong> CountReceivedSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}
