using System.Collections.Concurrent;
using PacCollector.Domain.Entities;
using PacCollector.Domain.Ports;

namespace PacCollector.Infrastructure.Persistence;

public sealed class InMemorySampleRepository : ISampleRepository
{
    private readonly ConcurrentDictionary<string, Sample> _byUuid = new(StringComparer.Ordinal);

    public Task SaveReceivedSampleAsync(Sample sample, CancellationToken ct = default)
    {
        _byUuid[sample.Uuid] = sample;
        return Task.CompletedTask;
    }

    public Task<Sample?> FindByUuidAsync(string uuid, CancellationToken ct = default)
    {
        _byUuid.TryGetValue(uuid, out var s);
        return Task.FromResult<Sample?>(s);
    }

    public Task<bool> ExistsForRunAsync(
        string serial,
        string sampleIdentifier,
        DateTimeOffset? startAt,
        CancellationToken ct = default)
    {
        var exists = _byUuid.Values.Any(s =>
            s.Serial.AsString == serial
            && s.SampleIdentifier == sampleIdentifier
            && s.StartAt == startAt);
        return Task.FromResult(exists);
    }

    public Task<IReadOnlyList<Sample>> ListPaginatedAsync(
        SampleQueryFilters filters,
        uint offset,
        uint limit,
        CancellationToken ct = default)
    {
        var ordered = _byUuid.Values
            .Where(s => Matches(s, filters))
            .OrderByDescending(s => s.ReceivedAt)
            .Skip((int)offset)
            .Take((int)limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Sample>>(ordered);
    }

    public Task<ulong> CountAsync(SampleQueryFilters filters, CancellationToken ct = default)
    {
        var count = (ulong)_byUuid.Values.Count(s => Matches(s, filters));
        return Task.FromResult(count);
    }

    public Task<ulong> CountReceivedSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var count = (ulong)_byUuid.Values.Count(s => s.ReceivedAt >= since);
        return Task.FromResult(count);
    }

    private static bool Matches(Sample s, SampleQueryFilters f)
    {
        if (f.Serial is not null && s.Serial.AsString != f.Serial) return false;
        if (f.Program is not null && s.Program != f.Program) return false;
        if (f.Operator is not null && s.Operator != f.Operator) return false;
        if (f.From is { } from && s.ReceivedAt < from) return false;
        if (f.To is { } to && s.ReceivedAt > to) return false;
        return true;
    }
}
