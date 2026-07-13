using PacCollector.Domain.Entities;

namespace PacCollector.Domain.Ports;

public interface IInstrumentRepository
{
    Task UpsertOnContactAsync(Instrument instrument, CancellationToken ct = default);
    Task<Instrument?> FindBySerialAsync(string serial, CancellationToken ct = default);
    Task UpdateAliasAsync(string serial, string? alias, CancellationToken ct = default);
    Task<IReadOnlyList<Instrument>> ListAllAsync(CancellationToken ct = default);
    Task IncrementSampleCountAsync(string serial, CancellationToken ct = default);
}
